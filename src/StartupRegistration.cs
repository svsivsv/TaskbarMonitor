using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;

namespace TaskbarMonitor
{
    internal static class StartupRegistration
    {
        private const string TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        internal static string BuildTaskXml(string executable, string userSid)
        {
            string path = SecurityElement.Escape(executable);
            string directory = SecurityElement.Escape(System.IO.Path.GetDirectoryName(executable));
            string user = SecurityElement.Escape(userSid);
            return "<Task version=\"1.2\" xmlns=\"" + TaskNamespace + "\">" +
                "<RegistrationInfo><Description>Taskbar Monitor automatic startup</Description></RegistrationInfo>" +
                "<Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + user +
                "</UserId><Delay>PT20S</Delay></LogonTrigger></Triggers>" +
                "<Principals><Principal id=\"CurrentUser\"><UserId>" + user +
                "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>" +
                "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>" +
                "<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>" +
                "<StartWhenAvailable>true</StartWhenAvailable><RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>" +
                "<AllowStartOnDemand>true</AllowStartOnDemand><Enabled>true</Enabled>" +
                "<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>" +
                "<RestartOnFailure><Interval>PT1M</Interval><Count>3</Count></RestartOnFailure></Settings>" +
                "<Actions Context=\"CurrentUser\"><Exec><Command>" + path +
                "</Command><Arguments>--startup</Arguments><WorkingDirectory>" + directory +
                "</WorkingDirectory></Exec></Actions></Task>";
        }

        private static object Call(object target, string method, params object[] args)
        {
            return target.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, target, args);
        }

        internal static void Apply(bool enabled, string executable)
        {
            string userSid;
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent()) userSid = identity.User.Value;
            string taskName = "TaskbarMonitor-" + userSid;
            object service = null, folder = null, task = null;
            try
            {
                service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true));
                Call(service, "Connect");
                folder = Call(service, "GetFolder", "\\");
                if (enabled)
                {
                    // Interactive token: current user only, no stored password or elevation.
                    task = Call(folder, "RegisterTask", taskName, BuildTaskXml(executable, userSid),
                        6, userSid, null, 3, null);
                }
                else
                {
                    try { Call(folder, "DeleteTask", taskName, 0); }
                    catch (TargetInvocationException ex)
                    {
                        COMException missing = ex.InnerException as COMException;
                        if (missing == null || missing.ErrorCode != unchecked((int)0x80070002)) throw;
                    }
                    catch (COMException ex)
                    {
                        if (ex.ErrorCode != unchecked((int)0x80070002)) throw;
                    }
                }
            }
            finally
            {
                if (task != null && Marshal.IsComObject(task)) Marshal.FinalReleaseComObject(task);
                if (folder != null && Marshal.IsComObject(folder)) Marshal.FinalReleaseComObject(folder);
                if (service != null && Marshal.IsComObject(service)) Marshal.FinalReleaseComObject(service);
            }
        }
    }
}
