using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.PolymorphicRelationships.Infrastructure.Query;

internal sealed class PolymorphicRelationalOptionsExtension : IDbContextOptionsExtension
{
    public PolymorphicRelationalOptionsExtension(bool experimentalSelectProjectionSupportEnabled = false)
    {
        ExperimentalSelectProjectionSupportEnabled = experimentalSelectProjectionSupportEnabled;
    }

    private DbContextOptionsExtensionInfo? _info;

    public bool ExperimentalSelectProjectionSupportEnabled { get; }

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
    {
        services.AddScoped<IMethodCallTranslatorPlugin, PolymorphicMethodCallTranslatorPlugin>();
    }

    public void Validate(IDbContextOptions options)
    {
    }

    public sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        private PolymorphicRelationalOptionsExtension TypedExtension => (PolymorphicRelationalOptionsExtension)Extension;

        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "using polymorphic relational translation ";

        public override int GetServiceProviderHashCode() => TypedExtension.ExperimentalSelectProjectionSupportEnabled ? 1 : 0;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            debugInfo["PolymorphicRelational"] = "1";
            debugInfo["PolymorphicExperimentalProjection"] = TypedExtension.ExperimentalSelectProjectionSupportEnabled ? "1" : "0";
        }

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
        {
            return other is ExtensionInfo otherInfo
                && TypedExtension.ExperimentalSelectProjectionSupportEnabled == ((PolymorphicRelationalOptionsExtension)otherInfo.Extension).ExperimentalSelectProjectionSupportEnabled;
        }
    }
}
