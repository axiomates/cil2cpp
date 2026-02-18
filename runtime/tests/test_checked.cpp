/**
 * Tests for checked arithmetic, unsigned comparisons, and checked conversions.
 * Covers ECMA-335 compliance for:
 *   - cgt.un / clt.un with NaN (III.1.5)
 *   - checked_add/sub/mul for int32 and int64 (III.3.1-6)
 *   - checked_conv / checked_conv_un with float sources (III.3.18)
 *   - to_unsigned() for all type categories including float
 */

#include <gtest/gtest.h>
#include <cil2cpp/cil2cpp.h>
#include <cmath>
#include <limits>

class CheckedTest : public ::testing::Test {
protected:
    void SetUp() override { cil2cpp::runtime_init(); }
    void TearDown() override { cil2cpp::runtime_shutdown(); }
};

// ===== to_unsigned float overloads =====

TEST_F(CheckedTest, ToUnsigned_Float_ReturnsIdentity) {
    EXPECT_FLOAT_EQ(cil2cpp::to_unsigned(3.14f), 3.14f);
    EXPECT_FLOAT_EQ(cil2cpp::to_unsigned(-1.0f), -1.0f);
}

TEST_F(CheckedTest, ToUnsigned_Double_ReturnsIdentity) {
    EXPECT_DOUBLE_EQ(cil2cpp::to_unsigned(3.14), 3.14);
    EXPECT_DOUBLE_EQ(cil2cpp::to_unsigned(-1.0), -1.0);
}

// ===== unsigned_gt / unsigned_lt — NaN semantics (ECMA-335 III.1.5) =====

TEST_F(CheckedTest, UnsignedGt_Double_NormalValues) {
    EXPECT_TRUE(cil2cpp::unsigned_gt(2.0, 1.0));
    EXPECT_FALSE(cil2cpp::unsigned_gt(1.0, 2.0));
    EXPECT_FALSE(cil2cpp::unsigned_gt(1.0, 1.0));
}

TEST_F(CheckedTest, UnsignedGt_Double_NaN_ReturnsTrue) {
    double nan = std::numeric_limits<double>::quiet_NaN();
    // cgt.un: returns true if either operand is NaN (unordered)
    EXPECT_TRUE(cil2cpp::unsigned_gt(nan, 1.0));
    EXPECT_TRUE(cil2cpp::unsigned_gt(1.0, nan));
    EXPECT_TRUE(cil2cpp::unsigned_gt(nan, nan));
}

TEST_F(CheckedTest, UnsignedLt_Double_NormalValues) {
    EXPECT_TRUE(cil2cpp::unsigned_lt(1.0, 2.0));
    EXPECT_FALSE(cil2cpp::unsigned_lt(2.0, 1.0));
    EXPECT_FALSE(cil2cpp::unsigned_lt(1.0, 1.0));
}

TEST_F(CheckedTest, UnsignedLt_Double_NaN_ReturnsTrue) {
    double nan = std::numeric_limits<double>::quiet_NaN();
    // clt.un: returns true if either operand is NaN (unordered)
    EXPECT_TRUE(cil2cpp::unsigned_lt(nan, 1.0));
    EXPECT_TRUE(cil2cpp::unsigned_lt(1.0, nan));
    EXPECT_TRUE(cil2cpp::unsigned_lt(nan, nan));
}

TEST_F(CheckedTest, UnsignedGt_Float_NaN_ReturnsTrue) {
    float nan = std::numeric_limits<float>::quiet_NaN();
    EXPECT_TRUE(cil2cpp::unsigned_gt(nan, 1.0f));
    EXPECT_TRUE(cil2cpp::unsigned_gt(1.0f, nan));
}

TEST_F(CheckedTest, UnsignedLt_Float_NaN_ReturnsTrue) {
    float nan = std::numeric_limits<float>::quiet_NaN();
    EXPECT_TRUE(cil2cpp::unsigned_lt(nan, 1.0f));
    EXPECT_TRUE(cil2cpp::unsigned_lt(1.0f, nan));
}

// Integer unsigned comparison still works
TEST_F(CheckedTest, UnsignedGt_Int32_SignedAsUnsigned) {
    // -1 as uint32 = 4294967295, which is > 0
    EXPECT_TRUE(cil2cpp::unsigned_gt(int32_t(-1), int32_t(0)));
    EXPECT_FALSE(cil2cpp::unsigned_gt(int32_t(0), int32_t(-1)));
}

TEST_F(CheckedTest, UnsignedLt_Int32_SignedAsUnsigned) {
    EXPECT_TRUE(cil2cpp::unsigned_lt(int32_t(0), int32_t(-1)));
    EXPECT_FALSE(cil2cpp::unsigned_lt(int32_t(-1), int32_t(0)));
}

// ===== checked_add / checked_sub / checked_mul — int64 =====

TEST_F(CheckedTest, CheckedAdd_Int64_Normal) {
    EXPECT_EQ(cil2cpp::checked_add(int64_t(100), int64_t(200)), int64_t(300));
}

TEST_F(CheckedTest, CheckedAdd_Int64_OverflowThrows) {
    cil2cpp::ExceptionContext ctx;
    ctx.previous = cil2cpp::g_exception_context;
    ctx.current_exception = nullptr;
    ctx.state = 0;
    cil2cpp::g_exception_context = &ctx;

    if (setjmp(ctx.jump_buffer) == 0) {
        cil2cpp::checked_add(std::numeric_limits<int64_t>::max(), int64_t(1));
        cil2cpp::g_exception_context = ctx.previous;
        FAIL() << "Expected OverflowException";
    } else {
        cil2cpp::g_exception_context = ctx.previous;
        ASSERT_NE(ctx.current_exception, nullptr);
    }
}

TEST_F(CheckedTest, CheckedSub_Int64_UnderflowThrows) {
    cil2cpp::ExceptionContext ctx;
    ctx.previous = cil2cpp::g_exception_context;
    ctx.current_exception = nullptr;
    ctx.state = 0;
    cil2cpp::g_exception_context = &ctx;

    if (setjmp(ctx.jump_buffer) == 0) {
        cil2cpp::checked_sub(std::numeric_limits<int64_t>::min(), int64_t(1));
        cil2cpp::g_exception_context = ctx.previous;
        FAIL() << "Expected OverflowException";
    } else {
        cil2cpp::g_exception_context = ctx.previous;
        ASSERT_NE(ctx.current_exception, nullptr);
    }
}

TEST_F(CheckedTest, CheckedMul_Int64_OverflowThrows) {
    cil2cpp::ExceptionContext ctx;
    ctx.previous = cil2cpp::g_exception_context;
    ctx.current_exception = nullptr;
    ctx.state = 0;
    cil2cpp::g_exception_context = &ctx;

    if (setjmp(ctx.jump_buffer) == 0) {
        cil2cpp::checked_mul(std::numeric_limits<int64_t>::max(), int64_t(2));
        cil2cpp::g_exception_context = ctx.previous;
        FAIL() << "Expected OverflowException";
    } else {
        cil2cpp::g_exception_context = ctx.previous;
        ASSERT_NE(ctx.current_exception, nullptr);
    }
}

TEST_F(CheckedTest, CheckedAddUn_Uint64_OverflowThrows) {
    cil2cpp::ExceptionContext ctx;
    ctx.previous = cil2cpp::g_exception_context;
    ctx.current_exception = nullptr;
    ctx.state = 0;
    cil2cpp::g_exception_context = &ctx;

    if (setjmp(ctx.jump_buffer) == 0) {
        cil2cpp::checked_add_un(std::numeric_limits<uint64_t>::max(), uint64_t(1));
        cil2cpp::g_exception_context = ctx.previous;
        FAIL() << "Expected OverflowException";
    } else {
        cil2cpp::g_exception_context = ctx.previous;
        ASSERT_NE(ctx.current_exception, nullptr);
    }
}

// ===== checked_conv / checked_conv_un — float source =====

TEST_F(CheckedTest, CheckedConv_FloatToInt32_InRange) {
    EXPECT_EQ((cil2cpp::checked_conv<int32_t, double>(42.0)), 42);
    EXPECT_EQ((cil2cpp::checked_conv<int32_t, float>(100.0f)), 100);
}

TEST_F(CheckedTest, CheckedConv_FloatToInt32_Overflow) {
    cil2cpp::ExceptionContext ctx;
    ctx.previous = cil2cpp::g_exception_context;
    ctx.current_exception = nullptr;
    ctx.state = 0;
    cil2cpp::g_exception_context = &ctx;

    if (setjmp(ctx.jump_buffer) == 0) {
        cil2cpp::checked_conv<int32_t, double>(3e10);
        cil2cpp::g_exception_context = ctx.previous;
        FAIL() << "Expected OverflowException";
    } else {
        cil2cpp::g_exception_context = ctx.previous;
        ASSERT_NE(ctx.current_exception, nullptr);
    }
}

TEST_F(CheckedTest, CheckedConvUn_FloatToUint32_InRange) {
    EXPECT_EQ((cil2cpp::checked_conv_un<uint32_t, double>(42.0)), uint32_t(42));
}

TEST_F(CheckedTest, CheckedConvUn_FloatToUint32_NegativeOverflow) {
    cil2cpp::ExceptionContext ctx;
    ctx.previous = cil2cpp::g_exception_context;
    ctx.current_exception = nullptr;
    ctx.state = 0;
    cil2cpp::g_exception_context = &ctx;

    if (setjmp(ctx.jump_buffer) == 0) {
        cil2cpp::checked_conv_un<uint32_t, double>(-1.0);
        cil2cpp::g_exception_context = ctx.previous;
        FAIL() << "Expected OverflowException";
    } else {
        cil2cpp::g_exception_context = ctx.previous;
        ASSERT_NE(ctx.current_exception, nullptr);
    }
}

// Helper macro: expects an OverflowException from expression
#define EXPECT_OVERFLOW(expr) do { \
    cil2cpp::ExceptionContext ctx; \
    ctx.previous = cil2cpp::g_exception_context; \
    ctx.current_exception = nullptr; \
    ctx.state = 0; \
    cil2cpp::g_exception_context = &ctx; \
    if (setjmp(ctx.jump_buffer) == 0) { \
        (void)(expr); \
        cil2cpp::g_exception_context = ctx.previous; \
        FAIL() << "Expected OverflowException from: " #expr; \
    } else { \
        cil2cpp::g_exception_context = ctx.previous; \
        ASSERT_NE(ctx.current_exception, nullptr); \
    } \
} while(0)

// ===== unsigned_ge / unsigned_le — NaN semantics (ECMA-335 III.3.6-12) =====

TEST_F(CheckedTest, UnsignedGe_Double_NormalValues) {
    EXPECT_TRUE(cil2cpp::unsigned_ge(2.0, 1.0));
    EXPECT_TRUE(cil2cpp::unsigned_ge(1.0, 1.0));
    EXPECT_FALSE(cil2cpp::unsigned_ge(0.5, 1.0));
}

TEST_F(CheckedTest, UnsignedGe_Double_NaN_ReturnsTrue) {
    double nan = std::numeric_limits<double>::quiet_NaN();
    // bge.un: branch if >= OR unordered (NaN)
    EXPECT_TRUE(cil2cpp::unsigned_ge(nan, 1.0));
    EXPECT_TRUE(cil2cpp::unsigned_ge(1.0, nan));
    EXPECT_TRUE(cil2cpp::unsigned_ge(nan, nan));
}

TEST_F(CheckedTest, UnsignedLe_Double_NormalValues) {
    EXPECT_TRUE(cil2cpp::unsigned_le(1.0, 2.0));
    EXPECT_TRUE(cil2cpp::unsigned_le(1.0, 1.0));
    EXPECT_FALSE(cil2cpp::unsigned_le(2.0, 0.5));
}

TEST_F(CheckedTest, UnsignedLe_Double_NaN_ReturnsTrue) {
    double nan = std::numeric_limits<double>::quiet_NaN();
    // ble.un: branch if <= OR unordered (NaN)
    EXPECT_TRUE(cil2cpp::unsigned_le(nan, 1.0));
    EXPECT_TRUE(cil2cpp::unsigned_le(1.0, nan));
    EXPECT_TRUE(cil2cpp::unsigned_le(nan, nan));
}

TEST_F(CheckedTest, UnsignedGe_Int32_SignedAsUnsigned) {
    // -1 as uint32 = 4294967295, which is >= 0
    EXPECT_TRUE(cil2cpp::unsigned_ge(int32_t(-1), int32_t(0)));
    EXPECT_TRUE(cil2cpp::unsigned_ge(int32_t(0), int32_t(0)));
}

TEST_F(CheckedTest, UnsignedLe_Int32_SignedAsUnsigned) {
    EXPECT_TRUE(cil2cpp::unsigned_le(int32_t(0), int32_t(-1)));
    EXPECT_TRUE(cil2cpp::unsigned_le(int32_t(0), int32_t(0)));
}

// ===== checked_conv — NaN throws OverflowException =====

TEST_F(CheckedTest, CheckedConv_NaN_ThrowsOverflow) {
    EXPECT_OVERFLOW((cil2cpp::checked_conv<int32_t, double>(std::numeric_limits<double>::quiet_NaN())));
}

TEST_F(CheckedTest, CheckedConv_NaN_Float_ThrowsOverflow) {
    EXPECT_OVERFLOW((cil2cpp::checked_conv<int32_t, float>(std::numeric_limits<float>::quiet_NaN())));
}

TEST_F(CheckedTest, CheckedConv_Infinity_ThrowsOverflow) {
    EXPECT_OVERFLOW((cil2cpp::checked_conv<int32_t, double>(std::numeric_limits<double>::infinity())));
}

TEST_F(CheckedTest, CheckedConv_NegInfinity_ThrowsOverflow) {
    EXPECT_OVERFLOW((cil2cpp::checked_conv<int32_t, double>(-std::numeric_limits<double>::infinity())));
}

TEST_F(CheckedTest, CheckedConvUn_NaN_ThrowsOverflow) {
    EXPECT_OVERFLOW((cil2cpp::checked_conv_un<uint32_t, double>(std::numeric_limits<double>::quiet_NaN())));
}

// ===== checked_conv — int64 boundary precision =====

TEST_F(CheckedTest, CheckedConv_DoubleToInt64_BoundaryThrows) {
    // 2^63 (9223372036854775808.0) does NOT fit in int64_t — must throw
    double two_63 = static_cast<double>(1ULL << 63); // = 9223372036854775808.0, exact
    EXPECT_OVERFLOW((cil2cpp::checked_conv<int64_t, double>(two_63)));
}

TEST_F(CheckedTest, CheckedConv_DoubleToInt64_MinValueAllowed) {
    // INT64_MIN (-9223372036854775808) = -2^63, exactly representable as double
    double int64_min = static_cast<double>(std::numeric_limits<int64_t>::min());
    EXPECT_EQ((cil2cpp::checked_conv<int64_t, double>(int64_min)), std::numeric_limits<int64_t>::min());
}

TEST_F(CheckedTest, CheckedConv_DoubleToInt64_LargestValidPasses) {
    // Largest double < 2^63: 9223372036854774784.0 (= 2^63 - 1024)
    // This is representable in int64_t.
    double largest_valid = 9223372036854774784.0;
    EXPECT_EQ((cil2cpp::checked_conv<int64_t, double>(largest_valid)), int64_t(9223372036854774784LL));
}

TEST_F(CheckedTest, CheckedConvUn_DoubleToUint64_BoundaryThrows) {
    // 2^64 (18446744073709551616.0) does NOT fit in uint64_t — must throw
    double two_64 = static_cast<double>(1ULL << 63) * 2.0;
    EXPECT_OVERFLOW((cil2cpp::checked_conv_un<uint64_t, double>(two_64)));
}
