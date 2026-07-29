using Dynamics365.BusinessCentral.Client;
using Dynamics365.BusinessCentral.Options;

namespace Dynamics365.BusinessCentral.Tests;

/// <summary>
/// Pins the jitter contract on <see cref="RetryHelper.ComputeDelay"/>: additive-only spread
/// on both the computed-backoff and the <c>Retry-After</c> branch, never negative, never
/// past <c>MaxDelay</c>, and fully deterministic at <c>JitterFactor = 0</c>.
/// </summary>
public class RetryDelayTests
{
    [Fact]
    public void RetryAfter_Is_Jittered_But_Never_Shortened()
    {
        var retry = new BusinessCentralRetryOptions
        {
            MaxDelay = TimeSpan.FromSeconds(30),
            JitterFactor = 0.5
        };

        var retryAfter = TimeSpan.FromSeconds(2);

        var delays = Enumerable.Range(0, 50)
            .Select(_ => RetryHelper.ComputeDelay(retry, retryAfter, attempt: 1))
            .ToList();

        // A Retry-After is a minimum wait — jitter must only ever extend it.
        Assert.All(delays, d => Assert.InRange(d, retryAfter, TimeSpan.FromSeconds(3)));

        // Concurrent callers handed the same Retry-After must not resume in lockstep.
        Assert.True(delays.Distinct().Count() > 1,
            "50 jittered delays collapsed to a single value");
    }

    [Fact]
    public void Computed_Backoff_Is_Jittered_Within_The_Same_Bounds()
    {
        var retry = new BusinessCentralRetryOptions
        {
            BaseDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(30),
            JitterFactor = 0.2
        };

        var delays = Enumerable.Range(0, 50)
            .Select(_ => RetryHelper.ComputeDelay(retry, retryAfter: null, attempt: 2))
            .ToList();

        // Attempt 2 doubles BaseDelay: baseline 2s, spread up to 2.4s.
        Assert.All(delays, d => Assert.InRange(d, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2.4)));
        Assert.True(delays.Distinct().Count() > 1);
    }

    [Fact]
    public void Jitter_Never_Exceeds_MaxDelay()
    {
        var retry = new BusinessCentralRetryOptions
        {
            MaxDelay = TimeSpan.FromSeconds(5),
            JitterFactor = 1.0
        };

        for (var i = 0; i < 50; i++)
        {
            var delay = RetryHelper.ComputeDelay(retry, TimeSpan.FromSeconds(5), attempt: 1);
            Assert.True(delay <= retry.MaxDelay);
        }
    }

    [Fact]
    public void Zero_JitterFactor_Is_Deterministic()
    {
        var retry = new BusinessCentralRetryOptions
        {
            BaseDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(30),
            JitterFactor = 0
        };

        Assert.Equal(TimeSpan.FromSeconds(4), RetryHelper.ComputeDelay(retry, null, attempt: 3));
        Assert.Equal(TimeSpan.FromSeconds(7), RetryHelper.ComputeDelay(retry, TimeSpan.FromSeconds(7), attempt: 1));
    }

    [Fact]
    public void Negative_JitterFactor_Is_Treated_As_Disabled()
    {
        var retry = new BusinessCentralRetryOptions
        {
            BaseDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(30),
            JitterFactor = -1
        };

        Assert.Equal(TimeSpan.FromSeconds(1), RetryHelper.ComputeDelay(retry, null, attempt: 1));
    }
}
