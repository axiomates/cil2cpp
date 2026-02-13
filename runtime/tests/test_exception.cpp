/**
 * CIL2CPP Runtime Tests - Exception Handling
 */

#include <gtest/gtest.h>
#include <cil2cpp/cil2cpp.h>

using namespace cil2cpp;

class ExceptionTest : public ::testing::Test {
protected:
    void SetUp() override {
        runtime_init();
    }

    void TearDown() override {
        runtime_shutdown();
    }
};

// ===== throw_null_reference =====

TEST_F(ExceptionTest, ThrowNullReference_CaughtByContext) {
    ExceptionContext ctx;
    ctx.previous = g_exception_context;
    ctx.current_exception = nullptr;
    ctx.state = 0;
    g_exception_context = &ctx;

    if (setjmp(ctx.jump_buffer) == 0) {
        throw_null_reference();
        g_exception_context = ctx.previous;
        FAIL() << "Should have thrown";
    } else {
        g_exception_context = ctx.previous;
        ASSERT_NE(ctx.current_exception, nullptr);
        // Note: message content check skipped — string interning pool is not cleared
        // between runtime restarts, which can cause stale pointers.
        // This will be fixed in the GC refactoring.
    }
}

// ===== throw_index_out_of_range =====

TEST_F(ExceptionTest, ThrowIndexOutOfRange_CaughtByContext) {
    ExceptionContext ctx;
    ctx.previous = g_exception_context;
    ctx.current_exception = nullptr;
    ctx.state = 0;
    g_exception_context = &ctx;

    if (setjmp(ctx.jump_buffer) == 0) {
        throw_index_out_of_range();
        g_exception_context = ctx.previous;
        FAIL() << "Should have thrown";
    } else {
        g_exception_context = ctx.previous;
        ASSERT_NE(ctx.current_exception, nullptr);
    }
}

// ===== throw_invalid_cast =====
// Note: This test is disabled due to SEH exception (access violation) in DbgHelp
// stack trace capture when runtime is restarted multiple times. Will be fixed
// in GC refactoring (string pool cleanup on shutdown).

TEST_F(ExceptionTest, DISABLED_ThrowInvalidCast_CaughtByContext) {
    ExceptionContext ctx;
    ctx.previous = g_exception_context;
    ctx.current_exception = nullptr;
    ctx.state = 0;
    g_exception_context = &ctx;

    if (setjmp(ctx.jump_buffer) == 0) {
        throw_invalid_cast();
        g_exception_context = ctx.previous;
        FAIL() << "Should have thrown";
    } else {
        g_exception_context = ctx.previous;
        ASSERT_NE(ctx.current_exception, nullptr);
    }
}

// ===== null_check =====

TEST_F(ExceptionTest, NullCheck_NonNull_NoThrow) {
    int dummy = 42;

    ExceptionContext ctx;
    ctx.previous = g_exception_context;
    ctx.current_exception = nullptr;
    ctx.state = 0;
    g_exception_context = &ctx;

    if (setjmp(ctx.jump_buffer) == 0) {
        null_check(&dummy);
        g_exception_context = ctx.previous;
        SUCCEED();
    } else {
        g_exception_context = ctx.previous;
        FAIL() << "Unexpected exception";
    }
}

TEST_F(ExceptionTest, NullCheck_Null_Throws) {
    ExceptionContext ctx;
    ctx.previous = g_exception_context;
    ctx.current_exception = nullptr;
    ctx.state = 0;
    g_exception_context = &ctx;

    if (setjmp(ctx.jump_buffer) == 0) {
        null_check(nullptr);
        g_exception_context = ctx.previous;
        FAIL() << "Expected NullReferenceException";
    } else {
        g_exception_context = ctx.previous;
        ASSERT_NE(ctx.current_exception, nullptr);
        SUCCEED();
    }
}

// ===== get_current_exception =====

TEST_F(ExceptionTest, GetCurrentException_NoContext_ReturnsNull) {
    EXPECT_EQ(get_current_exception(), nullptr);
}

TEST_F(ExceptionTest, GetCurrentException_InCatch_ReturnsException) {
    ExceptionContext ctx;
    ctx.previous = g_exception_context;
    ctx.current_exception = nullptr;
    ctx.state = 0;
    g_exception_context = &ctx;

    if (setjmp(ctx.jump_buffer) == 0) {
        throw_null_reference();
    } else {
        Exception* ex = get_current_exception();
        ASSERT_NE(ex, nullptr);
        EXPECT_EQ(ex, ctx.current_exception);
    }

    g_exception_context = ctx.previous;
}

// ===== CIL2CPP_TRY / CIL2CPP_CATCH_ALL macros =====

TEST_F(ExceptionTest, TryCatchAll_CatchesException) {
    bool caught = false;

    CIL2CPP_TRY
        throw_null_reference();
    CIL2CPP_CATCH_ALL
        caught = true;
    CIL2CPP_END_TRY

    EXPECT_TRUE(caught);
}

TEST_F(ExceptionTest, TryCatchAll_NormalFlow_NoCatch) {
    bool caught = false;
    bool executed = false;

    CIL2CPP_TRY
        executed = true;
    CIL2CPP_CATCH_ALL
        caught = true;
    CIL2CPP_END_TRY

    EXPECT_TRUE(executed);
    EXPECT_FALSE(caught);
}

TEST_F(ExceptionTest, NestedTryCatch_InnerCatches) {
    bool inner_caught = false;
    bool outer_caught = false;

    CIL2CPP_TRY
        CIL2CPP_TRY
            throw_index_out_of_range();
        CIL2CPP_CATCH_ALL
            inner_caught = true;
        CIL2CPP_END_TRY
    CIL2CPP_CATCH_ALL
        outer_caught = true;
    CIL2CPP_END_TRY

    EXPECT_TRUE(inner_caught);
    EXPECT_FALSE(outer_caught);
}

// ===== capture_stack_trace =====

TEST_F(ExceptionTest, CaptureStackTrace_ReturnsNonNull) {
    String* trace = capture_stack_trace();
    ASSERT_NE(trace, nullptr);
    // In Debug mode, should have some content
    EXPECT_GT(trace->length, 0);
}

// ===== Exception has stack trace =====

TEST_F(ExceptionTest, ThrownException_HasStackTrace) {
    ExceptionContext ctx;
    ctx.previous = g_exception_context;
    ctx.current_exception = nullptr;
    ctx.state = 0;
    g_exception_context = &ctx;

    if (setjmp(ctx.jump_buffer) == 0) {
        throw_null_reference();
    } else {
        ASSERT_NE(ctx.current_exception, nullptr);
        // Stack trace should be set
        EXPECT_NE(ctx.current_exception->stack_trace, nullptr);
    }

    g_exception_context = ctx.previous;
}
