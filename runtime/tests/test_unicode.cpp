/**
 * CIL2CPP Runtime Tests - Unicode (ICU4C backed)
 */

#include <gtest/gtest.h>
#include <cil2cpp/unicode.h>
#include <cil2cpp/string.h>
#include <cil2cpp/gc.h>
#include <cstring>

using namespace cil2cpp;

// ===== UTF-8 → UTF-16 Conversion =====

TEST(UnicodeConversion, Utf8ToUtf16_Ascii) {
    const char* utf8 = "Hello";
    Int32 len = unicode::utf8_to_utf16_length(utf8);
    EXPECT_EQ(len, 5);

    Char buf[16];
    Int32 written = unicode::utf8_to_utf16(utf8, buf, 16);
    EXPECT_EQ(written, 5);
    EXPECT_EQ(buf[0], u'H');
    EXPECT_EQ(buf[1], u'e');
    EXPECT_EQ(buf[4], u'o');
}

TEST(UnicodeConversion, Utf8ToUtf16_Empty) {
    const char* utf8 = "";
    Int32 len = unicode::utf8_to_utf16_length(utf8);
    EXPECT_EQ(len, 0);
}

TEST(UnicodeConversion, Utf8ToUtf16_Null) {
    EXPECT_EQ(unicode::utf8_to_utf16_length(nullptr), 0);
    EXPECT_EQ(unicode::utf8_to_utf16(nullptr, nullptr, 0), 0);
}

TEST(UnicodeConversion, Utf8ToUtf16_BMP) {
    // Chinese characters: U+4F60 U+597D (你好) = 3+3 = 6 bytes UTF-8, 2 code units
    const char* utf8 = "\xe4\xbd\xa0\xe5\xa5\xbd";
    Int32 len = unicode::utf8_to_utf16_length(utf8);
    EXPECT_EQ(len, 2);

    Char buf[4];
    unicode::utf8_to_utf16(utf8, buf, 4);
    EXPECT_EQ(buf[0], 0x4F60);
    EXPECT_EQ(buf[1], 0x597D);
}

TEST(UnicodeConversion, Utf8ToUtf16_Supplementary) {
    // U+1F600 (😀) = 4 bytes UTF-8, 2 UTF-16 code units (surrogate pair)
    const char* utf8 = "\xf0\x9f\x98\x80";
    Int32 len = unicode::utf8_to_utf16_length(utf8);
    EXPECT_EQ(len, 2);

    Char buf[4];
    unicode::utf8_to_utf16(utf8, buf, 4);
    EXPECT_EQ(buf[0], 0xD83D); // high surrogate
    EXPECT_EQ(buf[1], 0xDE00); // low surrogate
}

// ===== UTF-16 → UTF-8 Conversion =====

TEST(UnicodeConversion, Utf16ToUtf8_Ascii) {
    Char utf16[] = { u'A', u'B', u'C' };
    Int32 len = unicode::utf16_to_utf8_length(utf16, 3);
    EXPECT_EQ(len, 3);

    char buf[16];
    Int32 written = unicode::utf16_to_utf8(utf16, 3, buf, 16);
    EXPECT_EQ(written, 3);
    buf[written] = '\0';
    EXPECT_STREQ(buf, "ABC");
}

TEST(UnicodeConversion, Utf16ToUtf8_BMP) {
    Char utf16[] = { 0x4F60, 0x597D }; // 你好
    Int32 len = unicode::utf16_to_utf8_length(utf16, 2);
    EXPECT_EQ(len, 6);

    char buf[16];
    Int32 written = unicode::utf16_to_utf8(utf16, 2, buf, 16);
    buf[written] = '\0';
    EXPECT_STREQ(buf, "\xe4\xbd\xa0\xe5\xa5\xbd");
}

TEST(UnicodeConversion, Utf16ToUtf8_SurrogatePair) {
    Char utf16[] = { 0xD83D, 0xDE00 }; // U+1F600
    Int32 len = unicode::utf16_to_utf8_length(utf16, 2);
    EXPECT_EQ(len, 4);

    char buf[16];
    Int32 written = unicode::utf16_to_utf8(utf16, 2, buf, 16);
    buf[written] = '\0';
    EXPECT_STREQ(buf, "\xf0\x9f\x98\x80");
}

TEST(UnicodeConversion, Utf16ToUtf8_Empty) {
    EXPECT_EQ(unicode::utf16_to_utf8_length(nullptr, 0), 0);
    EXPECT_EQ(unicode::utf16_to_utf8(nullptr, 0, nullptr, 0), 0);
}

// ===== Character Classification =====

TEST(UnicodeCharClass, IsWhitespace_Ascii) {
    EXPECT_TRUE(unicode::is_whitespace(u' '));
    EXPECT_TRUE(unicode::is_whitespace(u'\t'));
    EXPECT_TRUE(unicode::is_whitespace(u'\n'));
    EXPECT_TRUE(unicode::is_whitespace(u'\r'));
    EXPECT_FALSE(unicode::is_whitespace(u'A'));
    EXPECT_FALSE(unicode::is_whitespace(u'0'));
}

TEST(UnicodeCharClass, IsWhitespace_Unicode) {
    // U+00A0 NO-BREAK SPACE
    EXPECT_TRUE(unicode::is_whitespace(0x00A0));
    // U+2003 EM SPACE
    EXPECT_TRUE(unicode::is_whitespace(0x2003));
    // U+3000 IDEOGRAPHIC SPACE
    EXPECT_TRUE(unicode::is_whitespace(0x3000));
}

TEST(UnicodeCharClass, IsDigit) {
    EXPECT_TRUE(unicode::is_digit(u'0'));
    EXPECT_TRUE(unicode::is_digit(u'9'));
    EXPECT_FALSE(unicode::is_digit(u'A'));
    EXPECT_FALSE(unicode::is_digit(u' '));
}

TEST(UnicodeCharClass, IsLetter) {
    EXPECT_TRUE(unicode::is_letter(u'A'));
    EXPECT_TRUE(unicode::is_letter(u'z'));
    EXPECT_TRUE(unicode::is_letter(0x4F60)); // 你
    EXPECT_FALSE(unicode::is_letter(u'0'));
    EXPECT_FALSE(unicode::is_letter(u' '));
}

TEST(UnicodeCharClass, IsLetterOrDigit) {
    EXPECT_TRUE(unicode::is_letter_or_digit(u'A'));
    EXPECT_TRUE(unicode::is_letter_or_digit(u'5'));
    EXPECT_FALSE(unicode::is_letter_or_digit(u' '));
    EXPECT_FALSE(unicode::is_letter_or_digit(u'!'));
}

TEST(UnicodeCharClass, IsUpper) {
    EXPECT_TRUE(unicode::is_upper(u'A'));
    EXPECT_TRUE(unicode::is_upper(u'Z'));
    EXPECT_FALSE(unicode::is_upper(u'a'));
    EXPECT_FALSE(unicode::is_upper(u'0'));
}

TEST(UnicodeCharClass, IsLower) {
    EXPECT_TRUE(unicode::is_lower(u'a'));
    EXPECT_TRUE(unicode::is_lower(u'z'));
    EXPECT_FALSE(unicode::is_lower(u'A'));
    EXPECT_FALSE(unicode::is_lower(u'0'));
}

TEST(UnicodeCharClass, IsPunctuation) {
    EXPECT_TRUE(unicode::is_punctuation(u'!'));
    EXPECT_TRUE(unicode::is_punctuation(u'.'));
    EXPECT_TRUE(unicode::is_punctuation(u','));
    EXPECT_FALSE(unicode::is_punctuation(u'A'));
    EXPECT_FALSE(unicode::is_punctuation(u' '));
}

TEST(UnicodeCharClass, IsControl) {
    EXPECT_TRUE(unicode::is_control(0x00)); // NUL
    EXPECT_TRUE(unicode::is_control(0x1F)); // US
    EXPECT_TRUE(unicode::is_control(0x7F)); // DEL
    EXPECT_FALSE(unicode::is_control(u'A'));
}

TEST(UnicodeCharClass, IsSurrogate) {
    EXPECT_TRUE(unicode::is_surrogate(0xD800));
    EXPECT_TRUE(unicode::is_surrogate(0xDBFF));
    EXPECT_TRUE(unicode::is_surrogate(0xDC00));
    EXPECT_TRUE(unicode::is_surrogate(0xDFFF));
    EXPECT_FALSE(unicode::is_surrogate(u'A'));
}

TEST(UnicodeCharClass, IsHighSurrogate) {
    EXPECT_TRUE(unicode::is_high_surrogate(0xD800));
    EXPECT_TRUE(unicode::is_high_surrogate(0xDBFF));
    EXPECT_FALSE(unicode::is_high_surrogate(0xDC00));
}

TEST(UnicodeCharClass, IsLowSurrogate) {
    EXPECT_TRUE(unicode::is_low_surrogate(0xDC00));
    EXPECT_TRUE(unicode::is_low_surrogate(0xDFFF));
    EXPECT_FALSE(unicode::is_low_surrogate(0xD800));
}

// ===== Case Conversion =====

TEST(UnicodeCaseConversion, ToUpper_Ascii) {
    EXPECT_EQ(unicode::to_upper(u'a'), u'A');
    EXPECT_EQ(unicode::to_upper(u'z'), u'Z');
    EXPECT_EQ(unicode::to_upper(u'A'), u'A'); // already upper
    EXPECT_EQ(unicode::to_upper(u'0'), u'0'); // non-letter unchanged
}

TEST(UnicodeCaseConversion, ToLower_Ascii) {
    EXPECT_EQ(unicode::to_lower(u'A'), u'a');
    EXPECT_EQ(unicode::to_lower(u'Z'), u'z');
    EXPECT_EQ(unicode::to_lower(u'a'), u'a'); // already lower
    EXPECT_EQ(unicode::to_lower(u'0'), u'0'); // non-letter unchanged
}

TEST(UnicodeCaseConversion, ToUpper_Unicode) {
    // Greek small letter alpha (U+03B1) → Greek capital letter alpha (U+0391)
    EXPECT_EQ(unicode::to_upper(0x03B1), static_cast<Char>(0x0391));
}

TEST(UnicodeCaseConversion, ToLower_Unicode) {
    // Greek capital letter alpha (U+0391) → Greek small letter alpha (U+03B1)
    EXPECT_EQ(unicode::to_lower(0x0391), static_cast<Char>(0x03B1));
}

// ===== ICalls =====

TEST(UnicodeICall, CharIsWhitespace) {
    EXPECT_EQ(unicode::char_is_whitespace(u' '), 1);
    EXPECT_EQ(unicode::char_is_whitespace(u'A'), 0);
}

TEST(UnicodeICall, CharToUpper) {
    EXPECT_EQ(unicode::char_to_upper(u'a'), u'A');
}

TEST(UnicodeICall, CharToLower) {
    EXPECT_EQ(unicode::char_to_lower(u'A'), u'a');
}

// ===== Integration: String functions using ICU =====

TEST(UnicodeStringIntegration, CreateUtf8_Ascii) {
    auto* str = string_create_utf8("Hello");
    ASSERT_NE(str, nullptr);
    EXPECT_EQ(str->length, 5);
    EXPECT_EQ(str->chars[0], u'H');
    EXPECT_EQ(str->chars[4], u'o');
}

TEST(UnicodeStringIntegration, CreateUtf8_Chinese) {
    auto* str = string_create_utf8("\xe4\xbd\xa0\xe5\xa5\xbd"); // 你好
    ASSERT_NE(str, nullptr);
    EXPECT_EQ(str->length, 2);
    EXPECT_EQ(str->chars[0], 0x4F60);
    EXPECT_EQ(str->chars[1], 0x597D);
}

TEST(UnicodeStringIntegration, ToUtf8_Roundtrip) {
    auto* str = string_create_utf8("Hello World!");
    char* utf8 = string_to_utf8(str);
    ASSERT_NE(utf8, nullptr);
    EXPECT_STREQ(utf8, "Hello World!");
    std::free(utf8);
}

TEST(UnicodeStringIntegration, ToUtf8_Roundtrip_BMP) {
    const char* original = "\xe4\xbd\xa0\xe5\xa5\xbd"; // 你好
    auto* str = string_create_utf8(original);
    char* utf8 = string_to_utf8(str);
    ASSERT_NE(utf8, nullptr);
    EXPECT_STREQ(utf8, original);
    std::free(utf8);
}

TEST(UnicodeStringIntegration, IsNullOrWhitespace_UnicodeSpaces) {
    // Create a string with only Unicode whitespace (NO-BREAK SPACE + EM SPACE)
    Char ws[] = { 0x00A0, 0x2003 };
    auto* str = string_create_utf16(ws, 2);
    EXPECT_TRUE(string_is_null_or_whitespace(str));
}

TEST(UnicodeStringIntegration, IsNullOrWhitespace_MixedContent) {
    auto* str = string_create_utf8("  hello  ");
    EXPECT_FALSE(string_is_null_or_whitespace(str));
}
