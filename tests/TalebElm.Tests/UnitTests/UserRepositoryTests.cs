using Microsoft.EntityFrameworkCore;
using TalebElm.Domain.Entities;
using TalebElm.Infrastructure.Repositories;
using TalebElm.Tests.Infrastructure;

namespace TalebElm.Tests.UnitTests;

public class UserRepositoryTests : SqliteTestBase
{
    [Fact]
    public async Task GetByIdAsync_WhenStored_ReturnsMatchingUser()
    {
        var user = NewUser("First");
        DbContext.Users.AddRange(user, NewUser("Other"));
        await SaveChangesAsync();
        ClearTracker();

        var result = await new UserRepository(DbContext).GetByIdAsync(user.Id);

        Assert.NotNull(result);
        Assert.Equal(user.Id, result.Id);
        Assert.Equal(user.Name, result.Name);
        Assert.Equal(user.Email, result.Email);
        Assert.Equal(user.JoinedAt, result.JoinedAt);
    }

    [Fact]
    public async Task GetByIdAsync_WhenMissing_ReturnsNull()
    {
        DbContext.Users.Add(NewUser("Other"));
        await SaveChangesAsync();
        ClearTracker();

        Assert.Null(await new UserRepository(DbContext).GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetAllAsync_WhenEmpty_ReturnsEmptyList()
    {
        Assert.Empty(await new UserRepository(DbContext).GetAllAsync());
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllStoredUsers()
    {
        var users = new[] { NewUser("First"), NewUser("Second") };
        DbContext.Users.AddRange(users);
        await SaveChangesAsync();
        ClearTracker();

        var results = await new UserRepository(DbContext).GetAllAsync();

        Assert.Equal(users.Select(u => u.Id).Order(), results.Select(u => u.Id).Order());
    }

    [Fact]
    public async Task AddAsync_StagesUserUntilUnitOfWorkSaves()
    {
        var user = NewUser("New");
        var unitOfWork = new UnitOfWork(DbContext);

        await unitOfWork.Users.AddAsync(user);

        Assert.Equal(EntityState.Added, DbContext.Entry(user).State);
        await using (var observer = CreateContext())
        {
            Assert.False(await observer.Users.AnyAsync(u => u.Id == user.Id));
        }

        await unitOfWork.SaveChangesAsync();
        await using var savedObserver = CreateContext();
        var saved = await savedObserver.Users.SingleAsync(u => u.Id == user.Id);
        Assert.Equal(user.Name, saved.Name);
        Assert.Equal(user.Email, saved.Email);
    }

    [Fact]
    public async Task Reads_DoNotSavePendingChanges()
    {
        var stored = NewUser("Stored");
        DbContext.Users.Add(stored);
        await SaveChangesAsync();
        ClearTracker();
        var pending = NewUser("Pending");
        DbContext.Users.Add(pending);
        var repository = new UserRepository(DbContext);

        await repository.GetByIdAsync(stored.Id);
        var all = await repository.GetAllAsync();

        Assert.Single(all);
        Assert.Equal(stored.Id, all[0].Id);
        await using var observer = CreateContext();
        Assert.False(await observer.Users.AnyAsync(u => u.Id == pending.Id));
        Assert.Equal(EntityState.Added, DbContext.Entry(pending).State);
    }

    private static User NewUser(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Email = $"{name.ToLowerInvariant()}@example.test",
        JoinedAt = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc)
    };
}
