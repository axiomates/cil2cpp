/**
 * CIL2CPP Runtime Tests - Hill-Climbing Algorithm
 * Pure algorithmic tests — no threads, no OS dependencies.
 */

#include <gtest/gtest.h>
#include <async/hill_climbing.h>

using namespace cil2cpp::threadpool::hill_climbing;

// ===== Initialization =====

TEST(HillClimbingTest, Init_SetsDefaults) {
    State s;
    init(s, 8, 4, 100);
    EXPECT_EQ(s.current_count, 8);
    EXPECT_EQ(s.min_threads, 4);
    EXPECT_EQ(s.max_threads, 100);
    EXPECT_EQ(s.direction, 1);
    EXPECT_EQ(s.warmup_remaining, State::kWarmupSamples);
    EXPECT_EQ(s.idle_intervals, 0);
    EXPECT_EQ(s.moves_in_direction, 0);
}

// ===== Warmup =====

TEST(HillClimbingTest, Warmup_NoChange) {
    State s;
    init(s, 4, 2, 16);

    // First warmup interval
    int adj = update(s, 100, 2, 0);
    EXPECT_EQ(adj, 0);
    EXPECT_EQ(s.warmup_remaining, 1);

    // Second warmup interval
    adj = update(s, 200, 3, 0);
    EXPECT_EQ(adj, 0);
    EXPECT_EQ(s.warmup_remaining, 0);
}

// ===== Starvation Detection =====

TEST(HillClimbingTest, Starvation_ForcesIncrease) {
    State s;
    init(s, 4, 2, 16);

    // Burn through warmup
    update(s, 100, 2, 0);
    update(s, 200, 3, 0);

    // All 4 workers active, queue has items → starvation
    int adj = update(s, 300, 4, 5);
    EXPECT_EQ(adj, +1);
    EXPECT_EQ(s.current_count, 5);
}

TEST(HillClimbingTest, Starvation_AtMax_NoChange) {
    State s;
    init(s, 16, 2, 16);

    update(s, 100, 8, 0);
    update(s, 200, 12, 0);

    // At max, all active, queue non-empty → can't inject
    int adj = update(s, 300, 16, 10);
    EXPECT_EQ(adj, 0);
    EXPECT_EQ(s.current_count, 16);
}

// ===== Over-provisioned Detection =====

TEST(HillClimbingTest, OverProvisioned_ShrinkAfterIdleIntervals) {
    State s;
    init(s, 8, 2, 16);

    update(s, 100, 4, 0);
    update(s, 200, 4, 0);

    // Simulate idle intervals: only 2 of 8 active (< half)
    update(s, 300, 2, 0);  // idle_intervals = 1
    int adj = update(s, 400, 2, 0);  // idle_intervals = 2
    EXPECT_EQ(adj, 0); // Not yet at threshold

    adj = update(s, 500, 2, 0);  // idle_intervals = 3 → shrink
    EXPECT_EQ(adj, -1);
    EXPECT_EQ(s.current_count, 7);
}

TEST(HillClimbingTest, OverProvisioned_AtMin_NoShrink) {
    State s;
    init(s, 2, 2, 16);

    update(s, 100, 1, 0);
    update(s, 200, 1, 0);

    // 3 idle intervals, but at min_threads
    update(s, 300, 0, 0);
    update(s, 400, 0, 0);
    int adj = update(s, 500, 0, 0);
    EXPECT_EQ(adj, 0);
    EXPECT_EQ(s.current_count, 2);
}

TEST(HillClimbingTest, OverProvisioned_ResetByActivity) {
    State s;
    init(s, 8, 2, 16);

    update(s, 100, 4, 0);
    update(s, 200, 4, 0);

    // 2 idle intervals
    update(s, 300, 2, 0);
    update(s, 400, 2, 0);

    // Then become active again — resets idle counter
    update(s, 500, 6, 0);

    // Another 2 idle intervals — not enough to trigger
    update(s, 600, 2, 0);
    int adj = update(s, 700, 2, 0);
    EXPECT_EQ(adj, 0); // Only 2 idle intervals, need 3
}

// ===== Throughput Improvement =====

TEST(HillClimbingTest, IncreasingThroughput_ContinuesDirection) {
    State s;
    init(s, 4, 2, 16);

    // Warmup with baseline throughput
    update(s, 100, 4, 0);  // 100 completions, throughput = 200/s
    update(s, 200, 4, 0);  // 100 completions, throughput = 200/s

    // Significant throughput increase (>1%)
    int adj = update(s, 350, 4, 0);  // 150 completions = 300/s, up from 200/s
    EXPECT_EQ(adj, +1);
    EXPECT_EQ(s.current_count, 5);
}

TEST(HillClimbingTest, DecreasingThroughput_ReversesDirection) {
    State s;
    init(s, 6, 2, 16);

    update(s, 100, 4, 0);
    update(s, 300, 4, 0); // throughput = 400/s baseline

    // Throughput drops significantly
    int adj = update(s, 350, 4, 0); // only 50 completions = 100/s, down from 400/s
    // Direction was +1, throughput dropped → reverse to -1
    EXPECT_EQ(adj, -1);
    EXPECT_EQ(s.current_count, 5);
    EXPECT_EQ(s.direction, -1);
}

TEST(HillClimbingTest, StableThroughput_NoChange) {
    State s;
    init(s, 4, 2, 16);

    update(s, 100, 3, 0);
    update(s, 200, 3, 0); // throughput = 200/s

    // Very similar throughput (within 1%)
    int adj = update(s, 300, 3, 0); // 100 completions = 200/s, same as before
    EXPECT_EQ(adj, 0);
    EXPECT_EQ(s.current_count, 4); // No change
}

// ===== Bounds Clamping =====

TEST(HillClimbingTest, AtMin_NeverDecrease) {
    State s;
    init(s, 2, 2, 16);
    s.direction = -1; // Force downward direction

    update(s, 100, 2, 0);
    update(s, 300, 2, 0); // 400/s

    // Throughput drop would normally cause -1
    int adj = update(s, 350, 2, 0); // 100/s, but already at min
    // Direction reversal might try -1, but clamped to min
    EXPECT_GE(s.current_count, s.min_threads);
}

TEST(HillClimbingTest, AtMax_NeverIncrease) {
    State s;
    init(s, 16, 2, 16);

    update(s, 100, 16, 0);
    update(s, 200, 16, 0);

    // Increasing throughput would normally inject
    int adj = update(s, 400, 16, 0);
    // At max — can't go higher (starvation check: queue_depth=0 so no starvation)
    EXPECT_LE(s.current_count, s.max_threads);
}

// ===== Max Consecutive Moves =====

TEST(HillClimbingTest, MaxConsecutiveMoves_HoldsAfterLimit) {
    State s;
    init(s, 4, 2, 16);

    // Warmup
    update(s, 100, 4, 0);
    update(s, 200, 4, 0);

    // 3 consecutive improving intervals (max consecutive moves = 3)
    // Each interval throughput is increasing
    int total_adj = 0;
    int64_t completions = 200;
    for (int i = 0; i < 5; i++) {
        // Increasing completions each interval to show improvement
        completions += 100 + i * 50;
        int adj = update(s, completions, 4, 0);
        total_adj += adj;
    }
    // Should have hit the max consecutive moves limit at some point
    EXPECT_LE(total_adj, State::kMaxConsecutiveMoves + 1); // At most 3-4 injections
}

// ===== Direction State =====

TEST(HillClimbingTest, DirectionPersistsAcrossUpdates) {
    State s;
    init(s, 8, 2, 16);
    EXPECT_EQ(s.direction, 1); // Initial direction: up

    update(s, 100, 4, 0);
    update(s, 200, 4, 0);

    // No change in direction during stable throughput
    update(s, 300, 4, 0);
    EXPECT_EQ(s.direction, 1); // Still up
}
