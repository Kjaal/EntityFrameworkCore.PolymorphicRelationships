using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.PolymorphicRelationships.Infrastructure;

internal static class PolymorphicRepairSaveScopeRegistry
{
    private static readonly ConditionalWeakTable<DbContext, RepairSaveScopeState> States = new();

    public static RepairSaveScopeState GetState(DbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        return States.GetValue(dbContext, static _ => new RepairSaveScopeState());
    }

    public sealed class RepairSaveScopeState
    {
        public bool IsActive { get; set; }

        public bool IsRepairSave { get; set; }

        public IDbContextTransaction? OwnedTransaction { get; set; }
    }
}
