/**
 * CIL2CPP Runtime - System.Math / System.MathF Internal Call Implementations
 *
 * .NET 8 Math methods are [InternalCall] with no IL body — the JIT replaces them
 * with CPU instructions. In AOT mode, we map them to <cmath> functions.
 */

#include <cil2cpp/types.h>
#include <cmath>
#include <cstdlib>

namespace cil2cpp {
namespace icall {

// ===== System.Math (double) =====

double Math_Abs_double(double value) { return std::fabs(value); }
float Math_Abs_float(float value) { return std::fabsf(value); }
int32_t Math_Abs_int(int32_t value) { return std::abs(value); }
int64_t Math_Abs_long(int64_t value) { return std::llabs(value); }

double Math_Sqrt(double value) { return std::sqrt(value); }
double Math_Sin(double value) { return std::sin(value); }
double Math_Cos(double value) { return std::cos(value); }
double Math_Tan(double value) { return std::tan(value); }
double Math_Asin(double value) { return std::asin(value); }
double Math_Acos(double value) { return std::acos(value); }
double Math_Atan(double value) { return std::atan(value); }
double Math_Atan2(double y, double x) { return std::atan2(y, x); }

double Math_Pow(double x, double y) { return std::pow(x, y); }
double Math_Exp(double value) { return std::exp(value); }
double Math_Log(double value) { return std::log(value); }
double Math_Log10(double value) { return std::log10(value); }
double Math_Log2(double value) { return std::log2(value); }

double Math_Floor(double value) { return std::floor(value); }
double Math_Ceiling(double value) { return std::ceil(value); }
double Math_Round(double value) { return std::round(value); }
double Math_Truncate(double value) { return std::trunc(value); }

double Math_Max_double(double a, double b) { return std::fmax(a, b); }
double Math_Min_double(double a, double b) { return std::fmin(a, b); }
int32_t Math_Max_int(int32_t a, int32_t b) { return a > b ? a : b; }
int32_t Math_Min_int(int32_t a, int32_t b) { return a < b ? a : b; }

double Math_Cbrt(double value) { return std::cbrt(value); }
double Math_IEEERemainder(double x, double y) { return std::remainder(x, y); }
double Math_FusedMultiplyAdd(double x, double y, double z) { return std::fma(x, y, z); }

// ===== System.MathF (float) =====

float MathF_Sqrt(float value) { return std::sqrtf(value); }
float MathF_Sin(float value) { return std::sinf(value); }
float MathF_Cos(float value) { return std::cosf(value); }
float MathF_Tan(float value) { return std::tanf(value); }
float MathF_Asin(float value) { return std::asinf(value); }
float MathF_Acos(float value) { return std::acosf(value); }
float MathF_Atan(float value) { return std::atanf(value); }
float MathF_Atan2(float y, float x) { return std::atan2f(y, x); }

float MathF_Pow(float x, float y) { return std::powf(x, y); }
float MathF_Exp(float value) { return std::expf(value); }
float MathF_Log(float value) { return std::logf(value); }
float MathF_Log10(float value) { return std::log10f(value); }
float MathF_Log2(float value) { return std::log2f(value); }

float MathF_Floor(float value) { return std::floorf(value); }
float MathF_Ceiling(float value) { return std::ceilf(value); }
float MathF_Round(float value) { return std::roundf(value); }
float MathF_Truncate(float value) { return std::truncf(value); }

float MathF_Max(float a, float b) { return std::fmaxf(a, b); }
float MathF_Min(float a, float b) { return std::fminf(a, b); }
float MathF_Cbrt(float value) { return std::cbrtf(value); }
float MathF_FusedMultiplyAdd(float x, float y, float z) { return std::fmaf(x, y, z); }

} // namespace icall
} // namespace cil2cpp
