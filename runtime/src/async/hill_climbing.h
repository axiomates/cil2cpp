/**
 * CIL2CPP Runtime - Hill-Climbing Thread Pool Algorithm
 * Simplified hill-climbing for dynamic thread count adjustment.
 * Measures throughput (completions/interval) and adjusts thread count
 * by +1/-1 based on feedback.
 */

#pragma once

#include <cstdint>

namespace cil2cpp::threadpool::hill_climbing {

struct State {
    int current_count;          // Current thread count (updated by caller after inject/retire)
    int min_threads;            // Floor (processor_count)
    int max_threads;            // Ceiling (default 1023)

    // Throughput tracking
    double last_throughput;     // Completions/sec at last sample
    int64_t last_completions;   // Total completions at last sample

    // Hill-climbing state
    int direction;              // +1 (climbing) or -1 (descending)
    int moves_in_direction;     // Consecutive moves in same direction
    int warmup_remaining;       // Warmup samples remaining (no adjustment)
    int idle_intervals;         // Consecutive intervals with < half workers active

    // Tuning constants
    static constexpr int kSampleIntervalMs = 500;
    static constexpr int kWarmupSamples = 2;
    static constexpr int kMaxConsecutiveMoves = 3;
    static constexpr double kImprovementThreshold = 0.01; // 1% improvement
    static constexpr int kIdleIntervalsBeforeShrink = 3;
};

/**
 * Initialize hill-climbing state.
 * @param s State to initialize
 * @param initial_count Starting thread count
 * @param min_threads Minimum thread count (floor)
 * @param max_threads Maximum thread count (ceiling)
 */
void init(State& s, int initial_count, int min_threads, int max_threads);

/**
 * Compute the recommended thread count adjustment.
 * Called once per sample interval (500ms) by the gate thread.
 *
 * @param s Hill-climbing state (mutated in place)
 * @param current_completions Monotonically increasing total completions
 * @param active_workers Number of workers currently executing work items
 * @param queue_depth Number of items waiting in the global queue
 * @return +1 (inject one thread), -1 (retire one thread), or 0 (no change)
 */
int update(State& s, int64_t current_completions, int active_workers, int queue_depth);

} // namespace cil2cpp::threadpool::hill_climbing
