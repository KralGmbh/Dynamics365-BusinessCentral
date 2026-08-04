using Dynamics365.BusinessCentral.Client;
using Dynamics365.BusinessCentral.OData;
using Dynamics365.BusinessCentral.Tests.Utils;

namespace Dynamics365.BusinessCentral.Tests;

/// <summary>
/// Pins the default-interface-method contract: a hand-written fake implementing a single
/// member compiles, composed members work through it, and everything else throws
/// <see cref="NotSupportedException"/> naming the member. Interface growth must never
/// break a consumer's test fake again.
/// </summary>
public class DefaultInterfaceTests
{
    /// <summary>Implements exactly one member — the typed-filter QueryAsync overload.</summary>
    private sealed class MinimalFake : IBusinessCentralClient
    {
        public List<TestEntity> Seed { get; init; } = [];
        public QueryOptions? LastOptions { get; private set; }

        public Task<List<TEntity>> QueryAsync<TEntity>(
            string path,
            ODataFilter? filter = null,
            Action<QueryOptions>? options = null,
            IEnumerable<string>? select = null,
            CancellationToken cancellationToken = default)
        {
            var opts = new QueryOptions();
            options?.Invoke(opts);
            LastOptions = opts;

            return Task.FromResult(Seed.Cast<TEntity>().ToList());
        }
    }

    [Fact]
    public async Task Minimal_Fake_Implements_One_Member_And_Compiles()
    {
        IBusinessCentralClient client = new MinimalFake
        {
            Seed = [new TestEntity { Id = 1 }]
        };

        var result = await client.QueryAsync<TestEntity>("orders");

        Assert.Single(result);
    }

    [Fact]
    public async Task FirstOrDefaultAsync_Composes_Over_QueryAsync()
    {
        var fake = new MinimalFake
        {
            Seed = [new TestEntity { Id = 1 }, new TestEntity { Id = 2 }]
        };

        IBusinessCentralClient client = fake;

        var first = await client.FirstOrDefaultAsync<TestEntity>("orders");

        Assert.NotNull(first);
        Assert.Equal(1, first.Id);
        Assert.Equal(1, fake.LastOptions?.Top);
    }

    [Fact]
    public async Task FirstOrDefaultAsync_Returns_Default_When_Nothing_Matches()
    {
        IBusinessCentralClient client = new MinimalFake();

        var first = await client.FirstOrDefaultAsync<TestEntity>("orders");

        Assert.Null(first);
    }

    [Fact]
    public async Task Unimplemented_Members_Throw_NotSupported_Naming_The_Member()
    {
        IBusinessCentralClient client = new MinimalFake();

        var company = Assert.Throws<NotSupportedException>(() => client.Company);
        Assert.Contains("Company", company.Message);

        var companies = await Assert.ThrowsAsync<NotSupportedException>(
            () => client.GetCompaniesAsync());
        Assert.Contains("GetCompaniesAsync", companies.Message);

        var metadata = await Assert.ThrowsAsync<NotSupportedException>(
            () => client.GetMetadataAsync());
        Assert.Contains("GetMetadataAsync", metadata.Message);

        var get = await Assert.ThrowsAsync<NotSupportedException>(
            () => client.GetAsync<TestEntity>("orders", "1"));
        Assert.Contains("GetAsync", get.Message);

        var delete = await Assert.ThrowsAsync<NotSupportedException>(
            () => client.DeleteAsync("orders", "1"));
        Assert.Contains("DeleteAsync", delete.Message);
    }
}
