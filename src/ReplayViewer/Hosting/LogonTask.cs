using System.Runtime.InteropServices;
using System.Security.Principal;

namespace ReplayViewer;

internal static class LogonTask
{
    public const string TaskName = "Replay Viewer";

    public static void Register(string targetExePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetExePath);
        var type =
            Type.GetTypeFromProgID("Schedule.Service")
            ?? throw new InvalidOperationException("Task Scheduler COM is unavailable.");
        dynamic service = Activator.CreateInstance(type)!;
        try
        {
            service.Connect();
            dynamic folder = service.GetFolder("\\");
            dynamic def = service.NewTask(0);
            def.RegistrationInfo.Description =
                "Starts Replay Viewer one minute after you sign in.";
            def.Settings.Enabled = true;
            def.Settings.Hidden = false;
            def.Settings.AllowDemandStart = true;
            def.Settings.DisallowStartIfOnBatteries = false;
            def.Settings.StopIfGoingOnBatteries = false;
            def.Settings.StartWhenAvailable = true;
            def.Settings.ExecutionTimeLimit = "PT0S";
            def.Settings.MultipleInstances = 2;
            def.Principal.LogonType = 3;
            def.Principal.RunLevel = 0;
            dynamic trigger = def.Triggers.Create(9);
            trigger.Enabled = true;
            trigger.Delay = "PT1M";
            var sid = WindowsIdentity.GetCurrent().User?.Value;
            if (!string.IsNullOrWhiteSpace(sid))
            {
                trigger.UserId = sid;
            }

            dynamic action = def.Actions.Create(0);
            action.Path = targetExePath;
            action.WorkingDirectory = Path.GetDirectoryName(targetExePath) ?? "";
            folder.RegisterTaskDefinition(TaskName, def, 6, null, null, 3);
        }
        finally
        {
            Marshal.FinalReleaseComObject(service);
        }
    }

    public static bool Exists()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service");
        if (type is null)
        {
            return false;
        }

        dynamic service = Activator.CreateInstance(type)!;
        try
        {
            service.Connect();
            dynamic folder = service.GetFolder("\\");
            try
            {
                folder.GetTask(TaskName);
                return true;
            }
            catch (Exception ex) when (ex is COMException or IOException)
            {
                // A missing task surfaces as FileNotFoundException through the COM binder.
                return false;
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(service);
        }
    }

    public static void Remove()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service");
        if (type is null)
        {
            return;
        }

        dynamic service = Activator.CreateInstance(type)!;
        try
        {
            service.Connect();
            dynamic folder = service.GetFolder("\\");
            try
            {
                folder.DeleteTask(TaskName, 0);
            }
            catch (Exception ex) when (ex is COMException or IOException)
            {
                // Already gone.
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(service);
        }
    }
}
