/**
 * CIL2CPP Runtime Tests - Globalization (ICU4C backed)
 *
 * Tests CompareInfo, String comparison, Ordinal, OrdinalCasing, TextInfo,
 * and GlobalizationMode ICalls.
 */

#include <gtest/gtest.h>
#include <cil2cpp/cil2cpp.h>
#include <cstring>

using namespace cil2cpp;

class GlobalizationTest : public ::testing::Test {
protected:
    void SetUp() override {
        runtime_init();
    }

    void TearDown() override {
        runtime_shutdown();
    }

    /// Helper to create a String* from a C string literal.
    static String* S(const char* utf8) {
        return string_create_utf8(utf8);
    }
};

// ===== CompareInfo.Compare (string, string, CompareOptions) =====
// CompareOptions: None=0, IgnoreCase=1, Ordinal=0x40000000, OrdinalIgnoreCase=0x10000000

TEST_F(GlobalizationTest, CompareInfo_Compare_Ordinal_Equal) {
    auto* a = S("Hello");
    auto* b = S("Hello");
    Int32 result = globalization::compareinfo_compare_string_string(nullptr, a, b, 0x40000000);
    EXPECT_EQ(result, 0);
}

TEST_F(GlobalizationTest, CompareInfo_Compare_Ordinal_LessThan) {
    auto* a = S("Apple");
    auto* b = S("Banana");
    Int32 result = globalization::compareinfo_compare_string_string(nullptr, a, b, 0x40000000);
    EXPECT_LT(result, 0);
}

TEST_F(GlobalizationTest, CompareInfo_Compare_Ordinal_GreaterThan) {
    auto* a = S("Banana");
    auto* b = S("Apple");
    Int32 result = globalization::compareinfo_compare_string_string(nullptr, a, b, 0x40000000);
    EXPECT_GT(result, 0);
}

TEST_F(GlobalizationTest, CompareInfo_Compare_CaseSensitive) {
    auto* a = S("hello");
    auto* b = S("Hello");
    // Ordinal: 'h' (0x68) > 'H' (0x48)
    Int32 result = globalization::compareinfo_compare_string_string(nullptr, a, b, 0x40000000);
    EXPECT_GT(result, 0);
}

TEST_F(GlobalizationTest, CompareInfo_Compare_IgnoreCase) {
    auto* a = S("hello");
    auto* b = S("HELLO");
    // CompareOptions.IgnoreCase = 1
    Int32 result = globalization::compareinfo_compare_string_string(nullptr, a, b, 1);
    EXPECT_EQ(result, 0);
}

TEST_F(GlobalizationTest, CompareInfo_Compare_OrdinalIgnoreCase) {
    auto* a = S("hello");
    auto* b = S("HELLO");
    // CompareOptions.OrdinalIgnoreCase = 0x10000000
    Int32 result = globalization::compareinfo_compare_string_string(nullptr, a, b, 0x10000000);
    EXPECT_EQ(result, 0);
}

TEST_F(GlobalizationTest, CompareInfo_Compare_NullStrings) {
    // Both null → equal
    Int32 result = globalization::compareinfo_compare_string_string(nullptr, nullptr, nullptr, 0);
    EXPECT_EQ(result, 0);

    // Null vs non-null
    auto* a = S("hello");
    EXPECT_LT(globalization::compareinfo_compare_string_string(nullptr, nullptr, a, 0), 0);
    EXPECT_GT(globalization::compareinfo_compare_string_string(nullptr, a, nullptr, 0), 0);
}

TEST_F(GlobalizationTest, CompareInfo_Compare_EmptyStrings) {
    auto* a = S("");
    auto* b = S("");
    Int32 result = globalization::compareinfo_compare_string_string(nullptr, a, b, 0x40000000);
    EXPECT_EQ(result, 0);
}

TEST_F(GlobalizationTest, CompareInfo_Compare_2Params) {
    auto* a = S("Hello");
    auto* b = S("Hello");
    Int32 result = globalization::compareinfo_compare_string_string_2(nullptr, a, b);
    EXPECT_EQ(result, 0);

    auto* c = S("Apple");
    auto* d = S("Banana");
    EXPECT_LT(globalization::compareinfo_compare_string_string_2(nullptr, c, d), 0);
}

// ===== CompareInfo.Compare (substring) =====

TEST_F(GlobalizationTest, CompareInfo_CompareSubstring) {
    auto* a = S("Hello World");
    auto* b = S("World");
    // Compare "World" (offset 6, length 5) with "World"
    Int32 result = globalization::compareinfo_compare_substring(
        nullptr, a, 6, 5, b, 0, 5, 0x40000000);
    EXPECT_EQ(result, 0);
}

// ===== CompareInfo.IndexOf =====

TEST_F(GlobalizationTest, CompareInfo_IndexOf_Found) {
    auto* source = S("Hello World");
    auto* value = S("World");
    Int32 result = globalization::compareinfo_index_of(nullptr, source, value, 0, 11, 0x40000000);
    EXPECT_EQ(result, 6);
}

TEST_F(GlobalizationTest, CompareInfo_IndexOf_NotFound) {
    auto* source = S("Hello World");
    auto* value = S("xyz");
    Int32 result = globalization::compareinfo_index_of(nullptr, source, value, 0, 11, 0x40000000);
    EXPECT_EQ(result, -1);
}

TEST_F(GlobalizationTest, CompareInfo_IndexOf_IgnoreCase) {
    auto* source = S("Hello World");
    auto* value = S("world");
    // OrdinalIgnoreCase
    Int32 result = globalization::compareinfo_index_of(nullptr, source, value, 0, 11, 0x10000000);
    EXPECT_EQ(result, 6);
}

TEST_F(GlobalizationTest, CompareInfo_IndexOf_StartIndex) {
    auto* source = S("abcabc");
    auto* value = S("abc");
    // Start searching from index 1
    Int32 result = globalization::compareinfo_index_of(nullptr, source, value, 1, 5, 0x40000000);
    EXPECT_EQ(result, 3);
}

// ===== CompareInfo.IsPrefix / IsSuffix =====

TEST_F(GlobalizationTest, CompareInfo_IsPrefix) {
    auto* source = S("Hello World");
    auto* prefix = S("Hello");
    EXPECT_TRUE(globalization::compareinfo_is_prefix(nullptr, source, prefix, 0x40000000));

    auto* notPrefix = S("World");
    EXPECT_FALSE(globalization::compareinfo_is_prefix(nullptr, source, notPrefix, 0x40000000));
}

TEST_F(GlobalizationTest, CompareInfo_IsSuffix) {
    auto* source = S("Hello World");
    auto* suffix = S("World");
    EXPECT_TRUE(globalization::compareinfo_is_suffix(nullptr, source, suffix, 0x40000000));

    auto* notSuffix = S("Hello");
    EXPECT_FALSE(globalization::compareinfo_is_suffix(nullptr, source, notSuffix, 0x40000000));
}

TEST_F(GlobalizationTest, CompareInfo_IsPrefix_IgnoreCase) {
    auto* source = S("Hello World");
    auto* prefix = S("hello");
    // OrdinalIgnoreCase
    EXPECT_TRUE(globalization::compareinfo_is_prefix(nullptr, source, prefix, 0x10000000));
}

TEST_F(GlobalizationTest, CompareInfo_IsSuffix_IgnoreCase) {
    auto* source = S("Hello World");
    auto* suffix = S("world");
    EXPECT_TRUE(globalization::compareinfo_is_suffix(nullptr, source, suffix, 0x10000000));
}

// ===== String.Compare =====
// StringComparison: CurrentCulture=0, CurrentCultureIgnoreCase=1,
//   InvariantCulture=2, InvariantCultureIgnoreCase=3, Ordinal=4, OrdinalIgnoreCase=5

TEST_F(GlobalizationTest, String_Compare_Ordinal) {
    auto* a = S("abc");
    auto* b = S("ABC");
    // Ordinal: 'a' > 'A'
    EXPECT_GT(globalization::string_compare_3(a, b, 4), 0);
}

TEST_F(GlobalizationTest, String_Compare_OrdinalIgnoreCase) {
    auto* a = S("abc");
    auto* b = S("ABC");
    EXPECT_EQ(globalization::string_compare_3(a, b, 5), 0);
}

TEST_F(GlobalizationTest, String_Compare_InvariantCulture) {
    auto* a = S("Hello");
    auto* b = S("Hello");
    EXPECT_EQ(globalization::string_compare_3(a, b, 2), 0);
}

TEST_F(GlobalizationTest, String_Compare_NullStrings) {
    EXPECT_EQ(globalization::string_compare_3(nullptr, nullptr, 4), 0);
    auto* a = S("a");
    EXPECT_LT(globalization::string_compare_3(nullptr, a, 4), 0);
    EXPECT_GT(globalization::string_compare_3(a, nullptr, 4), 0);
}

TEST_F(GlobalizationTest, String_Compare_6Params) {
    auto* a = S("Hello World");
    auto* b = S("World");
    // Compare a[6..5] with b[0..5], Ordinal
    EXPECT_EQ(globalization::string_compare_6(a, 6, b, 0, 5, 4), 0);
}

// ===== String.Equals / EndsWith / StartsWith =====

TEST_F(GlobalizationTest, String_Equals_Comparison) {
    auto* a = S("Hello");
    auto* b = S("hello");
    // OrdinalIgnoreCase
    EXPECT_TRUE(globalization::string_equals_comparison(a, b, 5));
    // Ordinal
    EXPECT_FALSE(globalization::string_equals_comparison(a, b, 4));
}

TEST_F(GlobalizationTest, String_EndsWith) {
    auto* str = S("Hello World");
    auto* suffix = S("World");
    // Ordinal
    EXPECT_TRUE(globalization::string_ends_with(str, suffix, 4));

    auto* notSuffix = S("Hello");
    EXPECT_FALSE(globalization::string_ends_with(str, notSuffix, 4));
}

TEST_F(GlobalizationTest, String_StartsWith) {
    auto* str = S("Hello World");
    auto* prefix = S("Hello");
    // Ordinal
    EXPECT_TRUE(globalization::string_starts_with(str, prefix, 4));

    auto* notPrefix = S("World");
    EXPECT_FALSE(globalization::string_starts_with(str, notPrefix, 4));
}

TEST_F(GlobalizationTest, String_EndsWith_IgnoreCase) {
    auto* str = S("Hello World");
    auto* suffix = S("world");
    // OrdinalIgnoreCase
    EXPECT_TRUE(globalization::string_ends_with(str, suffix, 5));
}

TEST_F(GlobalizationTest, String_StartsWith_IgnoreCase) {
    auto* str = S("Hello World");
    auto* prefix = S("hello");
    // OrdinalIgnoreCase
    EXPECT_TRUE(globalization::string_starts_with(str, prefix, 5));
}

// ===== Ordinal ICalls =====

TEST_F(GlobalizationTest, Ordinal_EqualsIgnoreCase) {
    Char a[] = {u'H', u'e', u'L', u'l', u'O'};
    Char b[] = {u'h', u'E', u'l', u'L', u'o'};
    EXPECT_TRUE(globalization::ordinal_equals_ignore_case(a, b, 5));

    Char c[] = {u'a', u'b', u'c'};
    Char d[] = {u'a', u'b', u'd'};
    EXPECT_FALSE(globalization::ordinal_equals_ignore_case(c, d, 3));
}

TEST_F(GlobalizationTest, Ordinal_CompareStringIgnoreCase) {
    Char a[] = {u'A', u'B', u'C'};
    Char b[] = {u'a', u'b', u'c'};
    EXPECT_EQ(globalization::ordinal_compare_ignore_case(a, 3, b, 3), 0);

    Char c[] = {u'a', u'b', u'c'};
    Char d[] = {u'a', u'b', u'd'};
    EXPECT_LT(globalization::ordinal_compare_ignore_case(c, 3, d, 3), 0);
}

// ===== OrdinalCasing =====

TEST_F(GlobalizationTest, OrdinalCasing_ToUpper) {
    EXPECT_EQ(globalization::ordinal_casing_to_upper(u'a'), u'A');
    EXPECT_EQ(globalization::ordinal_casing_to_upper(u'z'), u'Z');
    EXPECT_EQ(globalization::ordinal_casing_to_upper(u'A'), u'A');
    EXPECT_EQ(globalization::ordinal_casing_to_upper(u'1'), u'1');
}

TEST_F(GlobalizationTest, OrdinalCasing_InitTable_ReturnsNull) {
    // ICU-backed implementation returns nullptr (we don't use lookup tables)
    EXPECT_EQ(globalization::ordinal_casing_init_table(), nullptr);
}

TEST_F(GlobalizationTest, OrdinalCasing_InitPage_ReturnsNull) {
    EXPECT_EQ(globalization::ordinal_casing_init_page(0), nullptr);
    EXPECT_EQ(globalization::ordinal_casing_init_page(42), nullptr);
}

// ===== TextInfo.ChangeCaseCore =====

TEST_F(GlobalizationTest, TextInfo_ChangeCaseCore_ToUpper) {
    Char src[] = {u'h', u'e', u'l', u'l', u'o'};
    Char dst[5] = {};
    globalization::textinfo_change_case_core(nullptr, src, 5, dst, 5, true);
    EXPECT_EQ(dst[0], u'H');
    EXPECT_EQ(dst[1], u'E');
    EXPECT_EQ(dst[2], u'L');
    EXPECT_EQ(dst[3], u'L');
    EXPECT_EQ(dst[4], u'O');
}

TEST_F(GlobalizationTest, TextInfo_ChangeCaseCore_ToLower) {
    Char src[] = {u'H', u'E', u'L', u'L', u'O'};
    Char dst[5] = {};
    globalization::textinfo_change_case_core(nullptr, src, 5, dst, 5, false);
    EXPECT_EQ(dst[0], u'h');
    EXPECT_EQ(dst[1], u'e');
    EXPECT_EQ(dst[2], u'l');
    EXPECT_EQ(dst[3], u'l');
    EXPECT_EQ(dst[4], u'o');
}

TEST_F(GlobalizationTest, TextInfo_ChangeCaseCore_MixedCase) {
    Char src[] = {u'H', u'e', u'L', u'l', u'O'};
    Char dst[5] = {};
    globalization::textinfo_change_case_core(nullptr, src, 5, dst, 5, true);
    EXPECT_EQ(dst[0], u'H');
    EXPECT_EQ(dst[1], u'E');
    EXPECT_EQ(dst[2], u'L');
    EXPECT_EQ(dst[3], u'L');
    EXPECT_EQ(dst[4], u'O');
}

// ===== TextInfo.IcuChangeCase (alias for ChangeCaseCore) =====

TEST_F(GlobalizationTest, TextInfo_IcuChangeCase_ToUpper) {
    Char src[] = {u'a', u'b', u'c'};
    Char dst[3] = {};
    globalization::textinfo_icu_change_case(nullptr, src, 3, dst, 3, true);
    EXPECT_EQ(dst[0], u'A');
    EXPECT_EQ(dst[1], u'B');
    EXPECT_EQ(dst[2], u'C');
}

// ===== GlobalizationMode =====

TEST_F(GlobalizationTest, GlobalizationMode_UseNls_ReturnsFalse) {
    // We always use ICU, never NLS
    EXPECT_FALSE(globalization::globalization_mode_get_use_nls());
}

// ===== CompareInfo utility functions =====

TEST_F(GlobalizationTest, CompareInfo_GetNativeFlags) {
    // Just verify it returns a value and doesn't crash
    Int32 flags = globalization::compareinfo_get_native_flags(0);
    (void)flags; // don't care about exact value
}

TEST_F(GlobalizationTest, CompareInfo_CanUseAsciiOrdinal) {
    // None and IgnoreCase allow ASCII ordinal (options <= 1)
    EXPECT_TRUE(globalization::compareinfo_can_use_ascii_ordinal(0));
    EXPECT_TRUE(globalization::compareinfo_can_use_ascii_ordinal(1));
    // IgnoreNonSpace (2) and higher do not
    EXPECT_FALSE(globalization::compareinfo_can_use_ascii_ordinal(2));
}

TEST_F(GlobalizationTest, CompareInfo_CheckOptions_ValidDoesNotThrow) {
    // None = 0 — should not throw
    EXPECT_NO_THROW(globalization::compareinfo_check_options(0));
    // IgnoreCase = 1 — valid
    EXPECT_NO_THROW(globalization::compareinfo_check_options(1));
    // Ordinal = 0x40000000 — valid
    EXPECT_NO_THROW(globalization::compareinfo_check_options(0x40000000));
}

TEST_F(GlobalizationTest, String_GetCaseCompare) {
    // GetCaseCompareOfComparisonCulture returns the case-sensitivity flag:
    //   case-sensitive (Ordinal=4, CurrentCulture=0, InvariantCulture=2) → CompareOptions.None (0)
    //   case-insensitive (OrdinalIgnoreCase=5, etc.) → CompareOptions.IgnoreCase (1)
    EXPECT_EQ(globalization::string_get_case_compare(4), 0);  // Ordinal → None
    EXPECT_EQ(globalization::string_get_case_compare(5), 1);  // OrdinalIgnoreCase → IgnoreCase
    EXPECT_EQ(globalization::string_get_case_compare(0), 0);  // CurrentCulture → None
    EXPECT_EQ(globalization::string_get_case_compare(1), 1);  // CurrentCultureIgnoreCase → IgnoreCase
}

TEST_F(GlobalizationTest, String_CheckComparison_ValidDoesNotThrow) {
    for (int i = 0; i <= 5; i++) {
        EXPECT_NO_THROW(globalization::string_check_comparison(i));
    }
}

// ===== CompareInfo.GetCachedSortHandle =====

TEST_F(GlobalizationTest, CompareInfo_GetSortHandle) {
    auto* sortName = S("");
    intptr_t handle = globalization::compareinfo_get_sort_handle(sortName);
    // Should return a non-zero handle (pointer to cached collator)
    EXPECT_NE(handle, 0);
}

// ===== Unicode edge cases =====

TEST_F(GlobalizationTest, CompareInfo_Compare_Unicode) {
    // German sharp s (ß) vs "ss" — culture-sensitive comparison
    auto* a = S("\xc3\x9f");     // ß (U+00DF)
    auto* b = S("ss");
    // Ordinal: different
    EXPECT_NE(globalization::compareinfo_compare_string_string(nullptr, a, b, 0x40000000), 0);
    // Culture-sensitive (invariant, no IgnoreCase): ICU may treat ß ≈ ss
    // This depends on ICU version and locale — just verify no crash
    globalization::compareinfo_compare_string_string(nullptr, a, b, 0);
}

TEST_F(GlobalizationTest, CompareInfo_Compare_CJK) {
    // Chinese characters: 你 (U+4F60) vs 好 (U+597D)
    auto* a = S("\xe4\xbd\xa0");
    auto* b = S("\xe5\xa5\xbd");
    Int32 ordResult = globalization::compareinfo_compare_string_string(nullptr, a, b, 0x40000000);
    // Ordinal: 0x4F60 < 0x597D
    EXPECT_LT(ordResult, 0);
}

TEST_F(GlobalizationTest, String_Compare_TurkishI) {
    // Turkish I problem: İ (U+0130) vs i
    // Ordinal should NOT match
    auto* a = S("\xc4\xb0");  // İ (U+0130)
    auto* b = S("i");
    EXPECT_NE(globalization::string_compare_3(a, b, 4), 0);  // Ordinal
    // OrdinalIgnoreCase: İ.ToUpper != I.ToUpper, so still different
    // (ICU u_toupper of İ → İ, u_toupper of i → I)
    EXPECT_NE(globalization::string_compare_3(a, b, 5), 0);  // OrdinalIgnoreCase
}
