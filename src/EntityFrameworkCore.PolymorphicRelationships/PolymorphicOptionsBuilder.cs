namespace EntityFrameworkCore.PolymorphicRelationships;

public sealed class PolymorphicOptionsBuilder
{
    internal bool ExperimentalSelectProjectionSupportEnabled { get; private set; }

    public PolymorphicOptionsBuilder EnableExperimentalSelectProjectionSupport()
    {
        ExperimentalSelectProjectionSupportEnabled = true;
        return this;
    }
}
