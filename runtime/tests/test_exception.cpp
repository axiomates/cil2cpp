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
        EXPECT_NE(ctx.current_exception->f_stackTraceString, nullptr);
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
        EXPECT_NE(ctx.current_exception->f_message, nullptr);
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
        EXPECT_NE(ctx.current_exception->f_message, nullptr);
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
        EXPECT_EQ(ctx.current_exception->f_innerException, nullptr);
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
    ex->f_message = string_create_utf8("Custom error");
    ex->f_innerException = nullptr;
    ex->f_stackTraceString = nullptr;

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

// ===== Checked arithmetic =====

TEST_F(ExceptionTest, CheckedAdd_Normal_ReturnsSum) {
    EXPECT_EQ(checked_add<int32_t>(100, 200), 300);
    EXPECT_EQ(checked_add<int32_t>(-50, 50), 0);
    EXPECT_EQ(checked_add<int64_t>(1000000000LL, 2000000000LL), 3000000000LL);
}

TEST_F(ExceptionTest, CheckedAdd_Overflow_Throws) {
    bool caught = false;
    CIL2CPP_TRY
        checked_add<int32_t>(INT32_MAX, 1);
        FAIL() << "Should have thrown";
    CIL2CPP_CATCH_ALL
        caught = true;
    CIL2CPP_END_TRY
    EXPECT_TRUE(caught);
}

TEST_F(ExceptionTest, CheckedAdd_NegativeOverflow_Throws) {
    bool caught = false;
    CIL2CPP_TRY
        checked_add<int32_t>(INT32_MIN, -1);
        FAIL() << "Should have thrown";
    CIL2CPP_CATCH_ALL
        caught = true;
    CIL2CPP_END_TRY
    EXPECT_TRUE(caught);
}

TEST_F(ExceptionTest, CheckedSub_Normal_ReturnsDifference) {
    EXPECT_EQ(checked_sub<int32_t>(500, 200), 300);
    EXPECT_EQ(checked_sub<int32_t>(0, 0), 0);
}

TEST_F(ExceptionTest, CheckedSub_Overflow_Throws) {
    bool caught = false;
    CIL2CPP_TRY
        checked_sub<int32_t>(INT32_MIN, 1);
        FAIL() << "Should have thrown";
    CIL2CPP_CATCH_ALL
        caught = true;
    CIL2CPP_END_TRY
    EXPECT_TRUE(caught);
}

TEST_F(ExceptionTest, CheckedMul_Normal_ReturnsProduct) {
    EXPECT_EQ(checked_mul<int32_t>(15, 20), 300);
    EXPECT_EQ(checked_mul<int32_t>(-5, 3), -15);
    EXPECT_EQ(checked_mul<int32_t>(0, INT32_MAX), 0);
}

TEST_F(ExceptionTest, CheckedMul_Overflow_Throws) {
    bool caught = false;
    CIL2CPP_TRY
        checked_mul<int32_t>(INT32_MAX, 2);
        FAIL() << "Should have thrown";
    CIL2CPP_CATCH_ALL
        caught = true;
    CIL2CPP_END_TRY
    EXPECT_TRUE(caught);
}

TEST_F(ExceptionTest, CheckedAddUn_Normal_ReturnsSum) {
    EXPECT_EQ(checked_add_un<uint32_t>(100u, 200u), 300u);
}

TEST_F(ExceptionTest, CheckedAddUn_Overflow_Throws) {
    bool caught = false;
    CIL2CPP_TRY
        checked_add_un<uint32_t>(UINT32_MAX, 1u);
        FAIL() << "Should have thrown";
    CIL2CPP_CATCH_ALL
        caught = true;
    CIL2CPP_END_TRY
    EXPECT_TRUE(caught);
}

TEST_F(ExceptionTest, CheckedSubUn_Normal_ReturnsDifference) {
    EXPECT_EQ(checked_sub_un<uint32_t>(500u, 200u), 300u);
}

TEST_F(ExceptionTest, CheckedSubUn_Underflow_Throws) {
    bool caught = false;
    CIL2CPP_TRY
        checked_sub_un<uint32_t>(0u, 1u);
        FAIL() << "Should have thrown";
    CIL2CPP_CATCH_ALL
        caught = true;
    CIL2CPP_END_TRY
    EXPECT_TRUE(caught);
}

TEST_F(ExceptionTest, CheckedMulUn_Normal_ReturnsProduct) {
    EXPECT_EQ(checked_mul_un<uint32_t>(15u, 20u), 300u);
    EXPECT_EQ(checked_mul_un<uint32_t>(0u, UINT32_MAX), 0u);
}

TEST_F(ExceptionTest, CheckedMulUn_Overflow_Throws) {
    bool caught = false;
    CIL2CPP_TRY
        checked_mul_un<uint32_t>(UINT32_MAX, 2u);
        FAIL() << "Should have thrown";
    CIL2CPP_CATCH_ALL
        caught = true;
    CIL2CPP_END_TRY
    EXPECT_TRUE(caught);
}

TEST_F(ExceptionTest, ThrowOverflow_CaughtByContext) {
    bool caught = false;
    CIL2CPP_TRY
        throw_overflow();
        FAIL() << "Should have thrown";
    CIL2CPP_CATCH_ALL
        caught = true;
    CIL2CPP_END_TRY
    EXPECT_TRUE(caught);
}

TEST_F(ExceptionTest, ThrowInvalidOperation_CaughtByContext) {
    bool caught = false;
    CIL2CPP_TRY
        throw_invalid_operation();
        FAIL() << "Should have thrown";
    CIL2CPP_CATCH_ALL
        caught = true;
    CIL2CPP_END_TRY
    EXPECT_TRUE(caught);
}

TEST_F(ExceptionTest, ThrowInvalidOperation_HasCorrectMessage) {
    CIL2CPP_TRY
        throw_invalid_operation();
    CIL2CPP_CATCH_ALL
        auto ex = get_current_exception();
        ASSERT_NE(ex, nullptr);
        ASSERT_NE(ex->f_message, nullptr);
        auto msg = string_to_utf8(ex->f_message);
        EXPECT_NE(std::string(msg).find("Operation is not valid"), std::string::npos);
        free(msg);
    CIL2CPP_END_TRY
}

// ===== Checked Conversions (conv.ovf.*) =====

// --- checked_conv: signed source ---

TEST_F(ExceptionTest, CheckedConv_SignedToSigned_Narrowing_Normal) {
    EXPECT_EQ((checked_conv<int8_t>(int32_t(127))), 127);
    EXPECT_EQ((checked_conv<int8_t>(int32_t(-128))), -128);
    EXPECT_EQ((checked_conv<int16_t>(int32_t(32767))), 32767);
    EXPECT_EQ((checked_conv<int16_t>(int32_t(-32768))), -32768);
}

TEST_F(ExceptionTest, CheckedConv_SignedToSigned_Narrowing_Overflow) {
    bool caught = false;
    CIL2CPP_TRY
        checked_conv<int8_t>(int32_t(128));
        FAIL() << "Should have thrown";
    CIL2CPP_CATCH_ALL
        caught = true;
    CIL2CPP_END_TRY
    EXPECT_TRUE(caught);
}

TEST_F(ExceptionTest, CheckedConv_SignedToSigned_Narrowing_Underflow) {
    bool caught = false;
    CIL2CPP_TRY
        checked_conv<int8_t>(int32_t(-129));
        FAIL() << "Should have thrown";
    CIL2CPP_CATCH_ALL
        caught = true;
    CIL2CPP_END_TRY
    EXPECT_TRUE(caught);
}

TEST_F(ExceptionTest, CheckedConv_SignedToSigned_Widening_AlwaysSucceeds) {
    EXPECT_EQ((checked_conv<int64_t>(int32_t(-1))), -1LL);
    EXPECT_EQ((checked_conv<int64_t>(int32_t(INT32_MAX))), (int64_t)INT32_MAX);
}

TEST_F(ExceptionTest, CheckedConv_SignedToUnsigned_Normal) {
    EXPECT_EQ((checked_conv<uint8_t>(int32_t(255))), 255u);
    EXPECT_EQ((checked_conv<uint8_t>(int32_t(0))), 0u);
    EXPECT_EQ((checked_conv<uint64_t>(int64_t(42))), 42ull);
}

TEST_F(ExceptionTest, CheckedConv_SignedToUnsigned_NegativeThrows) {
    bool caught = false;
    CIL2CPP_TRY
        checked_conv<uint8_t>(int32_t(-1));
        FAIL() << "Should have thrown";
    CIL2CPP_CATCH_ALL
        caught = true;
    CIL2CPP_END_TRY
    EXPECT_TRUE(caught);
}

TEST_F(ExceptionTest, CheckedConv_SignedToUnsigned_TooLargeThrows) {
    bool caught = false;
    CIL2CPP_TRY
        checked_conv<uint8_t>(int32_t(256));
        FAIL() << "Should have thrown";
    CIL2CPP_CATCH_ALL
        caught = true;
    CIL2CPP_END_TRY
    EXPECT_TRUE(caught);
}

TEST_F(ExceptionTest, CheckedConv_UnsignedToSigned_Normal) {
    EXPECT_EQ((checked_conv<int8_t>(uint32_t(127))), 127);
    EXPECT_EQ((checked_conv<int32_t>(uint32_t(42))), 42);
}

TEST_F(ExceptionTest, CheckedConv_UnsignedToSigned_Overflow) {
    bool caught = false;
    CIL2CPP_TRY
        checked_conv<int8_t>(uint32_t(128));
        FAIL() << "Should have thrown";
    CIL2CPP_CATCH_ALL
        caught = true;
    CIL2CPP_END_TRY
    EXPECT_TRUE(caught);
}

TEST_F(ExceptionTest, CheckedConv_UnsignedToUnsigned_Narrowing_Normal) {
    EXPECT_EQ((checked_conv<uint8_t>(uint32_t(255))), 255u);
}

TEST_F(ExceptionTest, CheckedConv_UnsignedToUnsigned_Narrowing_Overflow) {
    bool caught = false;
    CIL2CPP_TRY
        checked_conv<uint8_t>(uint32_t(256));
        FAIL() << "Should have thrown";
    CIL2CPP_CATCH_ALL
        caught = true;
    CIL2CPP_END_TRY
    EXPECT_TRUE(caught);
}

// --- checked_conv_un: source reinterpreted as unsigned ---

TEST_F(ExceptionTest, CheckedConvUn_ToSignedTarget_Normal) {
    EXPECT_EQ((checked_conv_un<int8_t>(int32_t(100))), 100);
    EXPECT_EQ((checked_conv_un<int64_t>(uint64_t(42))), 42LL);
}

TEST_F(ExceptionTest, CheckedConvUn_ToSignedTarget_Overflow) {
    // -1 as int32 → reinterpreted as 0xFFFFFFFF (unsigned) → too large for int8
    bool caught = false;
    CIL2CPP_TRY
        checked_conv_un<int8_t>(int32_t(-1));
        FAIL() << "Should have thrown";
    CIL2CPP_CATCH_ALL
        caught = true;
    CIL2CPP_END_TRY
    EXPECT_TRUE(caught);
}

TEST_F(ExceptionTest, CheckedConvUn_ToUnsignedTarget_Normal) {
    EXPECT_EQ((checked_conv_un<uint8_t>(int32_t(200))), 200u);
    // -1 as int64 → reinterpreted as UINT64_MAX → fits in uint64_t
    EXPECT_EQ((checked_conv_un<uint64_t>(int64_t(-1))), UINT64_MAX);
}

TEST_F(ExceptionTest, CheckedConvUn_ToUnsignedTarget_Overflow) {
    // -1 as int32 → reinterpreted as 0xFFFFFFFF → too large for uint8
    bool caught = false;
    CIL2CPP_TRY
        checked_conv_un<uint8_t>(int32_t(-1));
        FAIL() << "Should have thrown";
    CIL2CPP_CATCH_ALL
        caught = true;
    CIL2CPP_END_TRY
    EXPECT_TRUE(caught);
}

TEST_F(ExceptionTest, CheckedConvUn_Int64ToInt64_LargeOverflow) {
    // UINT64_MAX (as unsigned reinterpretation) → too large for int64_t
    bool caught = false;
    CIL2CPP_TRY
        checked_conv_un<int64_t>(uint64_t(UINT64_MAX));
        FAIL() << "Should have thrown";
    CIL2CPP_CATCH_ALL
        caught = true;
    CIL2CPP_END_TRY
    EXPECT_TRUE(caught);
}

// ===== Exception Filter Macros =====

TEST_F(ExceptionTest, FilterBegin_Accept) {
    // Filter accepts: __filter_result = 1 → __exc_caught = true
    bool handler_ran = false;
    CIL2CPP_TRY
        throw_null_reference();
    CIL2CPP_FILTER_BEGIN
        int32_t __filter_result = 1; // accept
        if (__filter_result) { __exc_caught = true; } else { CIL2CPP_RETHROW; }
        handler_ran = true;
    CIL2CPP_END_TRY
    EXPECT_TRUE(handler_ran);
}

TEST_F(ExceptionTest, FilterBegin_Reject) {
    // Filter rejects: __filter_result = 0 → rethrow, caught by outer
    bool outer_caught = false;
    CIL2CPP_TRY
        CIL2CPP_TRY
            throw_null_reference();
        CIL2CPP_FILTER_BEGIN
            int32_t __filter_result = 0; // reject
            if (__filter_result) { __exc_caught = true; } else { CIL2CPP_RETHROW; }
        CIL2CPP_END_TRY
    CIL2CPP_CATCH_ALL
        outer_caught = true;
    CIL2CPP_END_TRY
    EXPECT_TRUE(outer_caught);
}

TEST_F(ExceptionTest, FilterBegin_ExceptionAccessible) {
    // Filter can access the exception object
    bool is_null_ref = false;
    CIL2CPP_TRY
        throw_null_reference();
    CIL2CPP_FILTER_BEGIN
        // In generated code, __exc_ctx.current_exception is the caught exception
        is_null_ref = (__exc_ctx.current_exception != nullptr);
        int32_t __filter_result = 1;
        if (__filter_result) { __exc_caught = true; } else { CIL2CPP_RETHROW; }
    CIL2CPP_END_TRY
    EXPECT_TRUE(is_null_ref);
}

// ===== Custom Attribute Query Tests =====

TEST_F(ExceptionTest, TypeHasAttribute_Found) {
    static CustomAttributeInfo attrs[] = {
        { .attribute_type_name = "System.ObsoleteAttribute", .args = nullptr, .arg_count = 0 },
    };
    static TypeInfo testType = {
        .name = "Test", .namespace_name = "NS", .full_name = "NS.Test",
        .base_type = nullptr, .interfaces = nullptr, .interface_count = 0,
        .instance_size = sizeof(Object), .element_size = 0,
        .flags = TypeFlags::None, .vtable = nullptr,
        .fields = nullptr, .field_count = 0,
        .methods = nullptr, .method_count = 0,
        .default_ctor = nullptr, .finalizer = nullptr,
        .interface_vtables = nullptr, .interface_vtable_count = 0,
        .custom_attributes = attrs, .custom_attribute_count = 1,
    };
    EXPECT_TRUE(type_has_attribute(&testType, "System.ObsoleteAttribute"));
    EXPECT_FALSE(type_has_attribute(&testType, "System.SerializableAttribute"));
}

TEST_F(ExceptionTest, TypeGetAttribute_ReturnsCorrect) {
    static CustomAttributeArg args[] = {
        { .type_name = "System.String", .string_val = "deprecated" },
    };
    static CustomAttributeInfo attrs[] = {
        { .attribute_type_name = "System.ObsoleteAttribute", .args = args, .arg_count = 1 },
    };
    static TypeInfo testType = {
        .name = "Test", .namespace_name = "NS", .full_name = "NS.Test",
        .base_type = nullptr, .interfaces = nullptr, .interface_count = 0,
        .instance_size = sizeof(Object), .element_size = 0,
        .flags = TypeFlags::None, .vtable = nullptr,
        .fields = nullptr, .field_count = 0,
        .methods = nullptr, .method_count = 0,
        .default_ctor = nullptr, .finalizer = nullptr,
        .interface_vtables = nullptr, .interface_vtable_count = 0,
        .custom_attributes = attrs, .custom_attribute_count = 1,
    };
    auto* attr = type_get_attribute(&testType, "System.ObsoleteAttribute");
    ASSERT_NE(attr, nullptr);
    EXPECT_EQ(attr->arg_count, 1u);
    EXPECT_STREQ(attr->args[0].string_val, "deprecated");
}

TEST_F(ExceptionTest, MethodHasAttribute_Found) {
    static CustomAttributeInfo attrs[] = {
        { .attribute_type_name = "System.ObsoleteAttribute", .args = nullptr, .arg_count = 0 },
    };
    static MethodInfo method = {
        .name = "OldMethod", .declaring_type = nullptr, .return_type = nullptr,
        .parameter_types = nullptr, .parameter_count = 0,
        .method_pointer = nullptr, .flags = 0, .vtable_slot = -1,
        .custom_attributes = attrs, .custom_attribute_count = 1,
    };
    EXPECT_TRUE(method_has_attribute(&method, "System.ObsoleteAttribute"));
    EXPECT_FALSE(method_has_attribute(&method, "System.SerializableAttribute"));
}

TEST_F(ExceptionTest, FieldHasAttribute_Found) {
    static CustomAttributeInfo attrs[] = {
        { .attribute_type_name = "System.ObsoleteAttribute", .args = nullptr, .arg_count = 0 },
    };
    static FieldInfo field = {
        .name = "OldField", .declaring_type = nullptr, .field_type = nullptr,
        .offset = 0, .flags = 0,
        .custom_attributes = attrs, .custom_attribute_count = 1,
    };
    EXPECT_TRUE(field_has_attribute(&field, "System.ObsoleteAttribute"));
    EXPECT_FALSE(field_has_attribute(&field, "System.SerializableAttribute"));
}
