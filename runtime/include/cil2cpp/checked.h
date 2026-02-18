/**
 * CIL2CPP Runtime - Checked Arithmetic
 * Implements overflow-checked arithmetic for C# 'checked' keyword.
 */

#pragma once

#include <cmath>
#include <cstdint>
#include <limits>
#include <type_traits>
#include "exception.h"

namespace cil2cpp {

// Signed checked arithmetic

template<typename T>
T checked_add(T a, T b) {
    static_assert(std::is_signed_v<T>, "Use checked_add_un for unsigned types");
    if (b > 0 && a > std::numeric_limits<T>::max() - b) throw_overflow();
    if (b < 0 && a < std::numeric_limits<T>::min() - b) throw_overflow();
    return a + b;
}

template<typename T>
T checked_sub(T a, T b) {
    static_assert(std::is_signed_v<T>, "Use checked_sub_un for unsigned types");
    if (b < 0 && a > std::numeric_limits<T>::max() + b) throw_overflow();
    if (b > 0 && a < std::numeric_limits<T>::min() + b) throw_overflow();
    return a - b;
}

template<typename T>
T checked_mul(T a, T b) {
    static_assert(std::is_signed_v<T>, "Use checked_mul_un for unsigned types");
    if (a == 0 || b == 0) return 0;
    if (a > 0) {
        if (b > 0) {
            if (a > std::numeric_limits<T>::max() / b) throw_overflow();
        } else {
            if (b < std::numeric_limits<T>::min() / a) throw_overflow();
        }
    } else {
        if (b > 0) {
            if (a < std::numeric_limits<T>::min() / b) throw_overflow();
        } else {
            if (a < std::numeric_limits<T>::max() / b) throw_overflow();
        }
    }
    return a * b;
}

// Unsigned checked arithmetic

template<typename T>
T checked_add_un(T a, T b) {
    static_assert(std::is_unsigned_v<T>, "Use checked_add for signed types");
    T result = a + b;
    if (result < a) throw_overflow();
    return result;
}

template<typename T>
T checked_sub_un(T a, T b) {
    static_assert(std::is_unsigned_v<T>, "Use checked_sub for signed types");
    if (a < b) throw_overflow();
    return a - b;
}

template<typename T>
T checked_mul_un(T a, T b) {
    static_assert(std::is_unsigned_v<T>, "Use checked_mul for signed types");
    if (a == 0 || b == 0) return 0;
    if (a > std::numeric_limits<T>::max() / b) throw_overflow();
    return a * b;
}

// ======== Checked conversions (conv.ovf.*) ========

/**
 * checked_conv: convert with overflow check (source treated as its natural signedness).
 * ECMA-335 III.3.18: conv.ovf.* (without _Un suffix)
 */
template<typename TTarget, typename TSource>
TTarget checked_conv(TSource value) {
    if constexpr (std::is_floating_point_v<TSource>) {
        // Float/double → integer: range-check directly.
        // is_signed_v/make_unsigned_t don't apply to floating-point types.
        if constexpr (std::is_signed_v<TTarget>) {
            if (value < static_cast<TSource>(std::numeric_limits<TTarget>::min()) ||
                value > static_cast<TSource>(std::numeric_limits<TTarget>::max()))
                throw_overflow();
        } else {
            if (value < static_cast<TSource>(0) ||
                value > static_cast<TSource>(std::numeric_limits<TTarget>::max()))
                throw_overflow();
        }
    } else if constexpr (std::is_signed_v<TSource> && std::is_signed_v<TTarget>) {
        // signed -> signed
        if constexpr (sizeof(TTarget) < sizeof(TSource)) {
            if (value < static_cast<TSource>(std::numeric_limits<TTarget>::min()) ||
                value > static_cast<TSource>(std::numeric_limits<TTarget>::max()))
                throw_overflow();
        }
    } else if constexpr (std::is_signed_v<TSource> && !std::is_signed_v<TTarget>) {
        // signed -> unsigned: value must be >= 0
        if (value < 0) throw_overflow();
        if constexpr (sizeof(TTarget) < sizeof(TSource)) {
            if (static_cast<std::make_unsigned_t<TSource>>(value) >
                static_cast<std::make_unsigned_t<TSource>>(std::numeric_limits<TTarget>::max()))
                throw_overflow();
        }
    } else if constexpr (!std::is_signed_v<TSource> && std::is_signed_v<TTarget>) {
        // unsigned -> signed: check <= TTarget::max
        if constexpr (sizeof(TSource) >= sizeof(TTarget)) {
            if (value > static_cast<TSource>(std::numeric_limits<TTarget>::max()))
                throw_overflow();
        }
    } else {
        // unsigned -> unsigned
        if constexpr (sizeof(TTarget) < sizeof(TSource)) {
            if (value > static_cast<TSource>(std::numeric_limits<TTarget>::max()))
                throw_overflow();
        }
    }
    return static_cast<TTarget>(value);
}

/**
 * checked_conv_un: convert with overflow check (source reinterpreted as unsigned).
 * ECMA-335 III.3.18: conv.ovf.*_Un suffix
 */
template<typename TTarget, typename TSource>
TTarget checked_conv_un(TSource value) {
    if constexpr (std::is_floating_point_v<TSource>) {
        // Float/double source: range-check directly (make_unsigned_t is invalid for floats).
        // Treat the float value as a non-negative number for unsigned interpretation.
        if (value < static_cast<TSource>(0) ||
            value > static_cast<TSource>(std::numeric_limits<TTarget>::max()))
            throw_overflow();
        return static_cast<TTarget>(value);
    } else {
        using USource = std::make_unsigned_t<TSource>;
        auto uval = static_cast<USource>(value);

        if constexpr (std::is_signed_v<TTarget>) {
            // unsigned -> signed target: check uval <= TTarget::max
            if (uval > static_cast<USource>(std::numeric_limits<TTarget>::max()))
                throw_overflow();
        } else {
            // unsigned -> unsigned target
            if constexpr (sizeof(TTarget) < sizeof(USource)) {
                if (uval > static_cast<USource>(std::numeric_limits<TTarget>::max()))
                    throw_overflow();
            }
        }
        return static_cast<TTarget>(uval);
    }
}

// ===== ckfinite (ECMA-335 III.3.19) =====
// Checks that a floating-point value is finite (not NaN or Infinity).
// Throws ArithmeticException if not. Returns the value unchanged.
inline double ckfinite(double val) {
    if (!std::isfinite(val)) {
        throw_arithmetic();
    }
    return val;
}

} // namespace cil2cpp
