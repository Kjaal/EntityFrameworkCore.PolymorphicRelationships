using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

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
        public bool IsExecutingWorkflow { get; set; }

        public bool IsRepairSave { get; set; }

        public PolymorphicPendingKeyRepairRegistry.PendingRepairBatch? PendingRepairs { get; set; }
    }
}
