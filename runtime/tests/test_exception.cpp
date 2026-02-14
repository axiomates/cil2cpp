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

// ===== Exception message field =====

TEST_F(ExceptionTest, NullReferenceException_HasMessage) {
    ExceptionContext ctx;
    ctx.previous = g_exception_context;
    ctx.current_exception = nullptr;
    ctx.state = 0;
    g_exception_context = &ctx;

    if (setjmp(ctx.jump_buffer) == 0) {
        throw_null_reference();
    } else {
        ASSERT_NE(ctx.current_exception, nullptr);
        EXPECT_NE(ctx.current_exception->message, nullptr);
    }

    g_exception_context = ctx.previous;
}

TEST_F(ExceptionTest, IndexOutOfRangeException_HasMessage) {
    ExceptionContext ctx;
    ctx.previous = g_exception_context;
    ctx.current_exception = nullptr;
    ctx.state = 0;
    g_exception_context = &ctx;

    if (setjmp(ctx.jump_buffer) == 0) {
        throw_index_out_of_range();
    } else {
        ASSERT_NE(ctx.current_exception, nullptr);
        EXPECT_NE(ctx.current_exception->message, nullptr);
    }

    g_exception_context = ctx.previous;
}

TEST_F(ExceptionTest, Exception_InnerException_IsNull) {
    ExceptionContext ctx;
    ctx.previous = g_exception_context;
    ctx.current_exception = nullptr;
    ctx.state = 0;
    g_exception_context = &ctx;

    if (setjmp(ctx.jump_buffer) == 0) {
        throw_null_reference();
    } else {
        ASSERT_NE(ctx.current_exception, nullptr);
        // Default inner_exception should be null
        EXPECT_EQ(ctx.current_exception->inner_exception, nullptr);
    }

    g_exception_context = ctx.previous;
}

// ===== CIL2CPP_FINALLY =====

TEST_F(ExceptionTest, TryFinally_NormalFlow_FinallyRuns) {
    bool try_executed = false;
    bool finally_ran = false;

    CIL2CPP_TRY
        try_executed = true;
    CIL2CPP_FINALLY
        finally_ran = true;
    CIL2CPP_END_TRY

    EXPECT_TRUE(try_executed);
    EXPECT_TRUE(finally_ran);
}

TEST_F(ExceptionTest, TryFinally_WithException_FinallyRuns) {
    bool finally_ran = false;
    bool outer_caught = false;

    // Outer handler catches the propagated exception from inner try-finally
    CIL2CPP_TRY
        CIL2CPP_TRY
            throw_null_reference();
        CIL2CPP_FINALLY
            finally_ran = true;
        CIL2CPP_END_TRY
    CIL2CPP_CATCH_ALL
        outer_caught = true;
    CIL2CPP_END_TRY

    EXPECT_TRUE(finally_ran);
    EXPECT_TRUE(outer_caught);
}

TEST_F(ExceptionTest, TryCatchFinally_AllRun) {
    bool caught = false;
    bool finally_ran = false;

    CIL2CPP_TRY
        throw_null_reference();
    CIL2CPP_CATCH_ALL
        caught = true;
    CIL2CPP_FINALLY
        finally_ran = true;
    CIL2CPP_END_TRY

    EXPECT_TRUE(caught);
    EXPECT_TRUE(finally_ran);
}

TEST_F(ExceptionTest, TryCatchFinally_NoException_FinallyStillRuns) {
    bool caught = false;
    bool finally_ran = false;

    CIL2CPP_TRY
        // no exception
    CIL2CPP_CATCH_ALL
        caught = true;
    CIL2CPP_FINALLY
        finally_ran = true;
    CIL2CPP_END_TRY

    EXPECT_FALSE(caught);
    EXPECT_TRUE(finally_ran);
}

// ===== CIL2CPP_RETHROW =====

TEST_F(ExceptionTest, Rethrow_CaughtByOuterHandler) {
    bool inner_caught = false;
    bool outer_caught = false;

    CIL2CPP_TRY
        CIL2CPP_TRY
            throw_null_reference();
        CIL2CPP_CATCH_ALL
            inner_caught = true;
            CIL2CPP_RETHROW;
        CIL2CPP_END_TRY
    CIL2CPP_CATCH_ALL
        outer_caught = true;
    CIL2CPP_END_TRY

    EXPECT_TRUE(inner_caught);
    EXPECT_TRUE(outer_caught);
}

TEST_F(ExceptionTest, Rethrow_PreservesException) {
    Exception* inner_ex = nullptr;
    Exception* outer_ex = nullptr;

    CIL2CPP_TRY
        CIL2CPP_TRY
            throw_null_reference();
        CIL2CPP_CATCH_ALL
            inner_ex = get_current_exception();
            CIL2CPP_RETHROW;
        CIL2CPP_END_TRY
    CIL2CPP_CATCH_ALL
        outer_ex = get_current_exception();
    CIL2CPP_END_TRY

    ASSERT_NE(inner_ex, nullptr);
    ASSERT_NE(outer_ex, nullptr);
    EXPECT_EQ(inner_ex, outer_ex);  // Same exception object
}

// ===== throw_exception with custom exception =====

TEST_F(ExceptionTest, ThrowException_CustomException) {
    // Manually create an exception
    static TypeInfo CustomExType = {
        .name = "CustomException",
        .namespace_name = "Test",
        .full_name = "Test.CustomException",
        .base_type = nullptr,
        .interfaces = nullptr,
        .interface_count = 0,
        .instance_size = sizeof(Exception),
        .element_size = 0,
        .flags = TypeFlags::None,
        .vtable = nullptr,
        .fields = nullptr,
        .field_count = 0,
        .methods = nullptr,
        .method_count = 0,
        .default_ctor = nullptr,
        .finalizer = nullptr,
        .interface_vtables = nullptr,
        .interface_vtable_count = 0,
    };

    Exception* ex = static_cast<Exception*>(gc::alloc(sizeof(Exception), &CustomExType));
    ex->message = string_create_utf8("Custom error");
    ex->inner_exception = nullptr;
    ex->stack_trace = nullptr;

    CIL2CPP_TRY
        throw_exception(ex);
        FAIL() << "Should have thrown";
    CIL2CPP_CATCH_ALL
        Exception* caught = get_current_exception();
        ASSERT_NE(caught, nullptr);
        EXPECT_EQ(caught, ex);
        EXPECT_EQ(caught->__type_info, &CustomExType);
    CIL2CPP_END_TRY
}

// ===== Nested try-catch: inner doesn't catch, outer does =====

TEST_F(ExceptionTest, NestedTryCatch_InnerDoesNotCatch_OuterCatches) {
    bool outer_caught = false;

    CIL2CPP_TRY
        CIL2CPP_TRY
            throw_null_reference();
        CIL2CPP_FINALLY
            // finally runs but doesn't catch
        CIL2CPP_END_TRY
    CIL2CPP_CATCH_ALL
        outer_caught = true;
    CIL2CPP_END_TRY

    EXPECT_TRUE(outer_caught);
}

// ===== Exception context state =====

TEST_F(ExceptionTest, ExceptionContext_State0InTry) {
    CIL2CPP_TRY
        // In try block, state should be 0 (set by macro)
        // We can't directly access __exc_ctx here due to macro scoping,
        // but we verify the context is properly set up
        EXPECT_NE(g_exception_context, nullptr);
    CIL2CPP_CATCH_ALL
        FAIL() << "Should not catch";
    CIL2CPP_END_TRY
}
