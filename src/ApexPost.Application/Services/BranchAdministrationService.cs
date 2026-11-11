using ApexPost.Domain;

namespace ApexPost.Application;

/// <summary>Input for creating or editing a branch (explicit command, never the entity itself).</summary>
public sealed record SaveBranchCommand(string Code, string Name, string Region, string OpeningHours, string CounterName);

public interface IBranchAdministrationService
{
    /// <summary>Creates an active branch with its first counter. Duplicate codes raise <see cref="DomainException"/>.</summary>
    Task<Branch> CreateAsync(SaveBranchCommand command, CancellationToken cancellationToken = default);

    /// <summary>Updates the editable branch details; returns null when the branch does not exist.</summary>
    Task<Branch?> UpdateAsync(string id, SaveBranchCommand command, CancellationToken cancellationToken = default);
}

/// <summary>Branch maintenance use cases. One commit per use case through the unit of work.</summary>
public sealed class BranchAdministrationService(IBranchRepository branches, IUnitOfWork unitOfWork) : IBranchAdministrationService
{
    public async Task<Branch> CreateAsync(SaveBranchCommand command, CancellationToken cancellationToken = default)
    {
        var code = command.Code.Trim().ToUpperInvariant();
        if (await branches.GetByCodeAsync(code, cancellationToken) is not null)
            throw new DomainException("Branch code already exists.");

        var branch = new Branch
        {
            Code = code,
            Name = command.Name.Trim(),
            Region = command.Region.Trim(),
            OpeningHours = command.OpeningHours.Trim(),
            Status = BranchStatus.Active,
            Counters = [new Counter(Guid.NewGuid().ToString("N"), command.CounterName.Trim())]
        };
        await branches.AddAsync(branch, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return branch;
    }

    public async Task<Branch?> UpdateAsync(string id, SaveBranchCommand command, CancellationToken cancellationToken = default)
    {
        var branch = await branches.GetByIdAsync(id, cancellationToken);
        if (branch is null)
            return null;

        branch.Name = command.Name.Trim();
        branch.Region = command.Region.Trim();
        branch.OpeningHours = command.OpeningHours.Trim();
        if (branch.Counters.Count == 0)
            branch.Counters.Add(new Counter(Guid.NewGuid().ToString("N"), command.CounterName.Trim()));
        else
            branch.Counters[0] = branch.Counters[0] with { Name = command.CounterName.Trim() };

        await branches.UpdateAsync(branch, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return branch;
    }
}
