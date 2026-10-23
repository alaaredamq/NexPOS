namespace Common.Licensing;

/// <summary>Reads the identity of the PC the app is running on.</summary>
public interface IMachineIdProvider
{
    MachineId GetMachineId();
}
