using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.AccessControl;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DxgiLatencyShare
{
    public static class NamedEvent
    {
        public static EventWaitHandle CreateOrOpenNamedEvent(string eventName, bool initialValue, bool autoReset)
        {
            if (string.IsNullOrWhiteSpace(eventName))
            {
                throw new ArgumentNullException(nameof(eventName));
            }

            // code is copied from https://docs.microsoft.com/en-us/dotnet/api/system.threading.eventwaithandle.openexisting?view=net-6.0

            EventWaitHandle ewh = null;
            bool doesNotExist = false;
            bool unauthorized = false;

            // The value of this variable is set by the event
            // constructor. It is true if the named system event was
            // created, and false if the named event already existed.
            //
            bool wasCreated;

            // Attempt to open the named event.
            try
            {
                // Open the event with (EventWaitHandleRights.Synchronize
                // | EventWaitHandleRights.Modify), to wait on and 
                // signal the named event.
                //
                ewh = EventWaitHandle.OpenExisting(eventName);
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                doesNotExist = true;
            }
            catch (UnauthorizedAccessException ex)
            {
                unauthorized = true;
            }

            // There are three cases: (1) The event does not exist.
            // (2) The event exists, but the current user doesn't 
            // have access. (3) The event exists and the user has
            // access.
            //
            if (doesNotExist)
            {
                // The event does not exist, so create it.

                // Create an access control list (ACL) that denies the
                // current user the right to wait on or signal the 
                // event, but allows the right to read and change
                // security information for the event.
                //
                string user = Environment.UserDomainName + "\\" + Environment.UserName;
                EventWaitHandleSecurity ewhSec = new EventWaitHandleSecurity();

                EventWaitHandleAccessRule rule =
                    new EventWaitHandleAccessRule(user,
                        EventWaitHandleRights.Synchronize | EventWaitHandleRights.Modify,
                        AccessControlType.Deny);
                ewhSec.AddAccessRule(rule);

                rule = new EventWaitHandleAccessRule(user,
                    EventWaitHandleRights.ReadPermissions | EventWaitHandleRights.ChangePermissions,
                    AccessControlType.Allow);
                ewhSec.AddAccessRule(rule);

                // Create an EventWaitHandle object that represents
                // the system event named by the parameter 'eventName', 
                // initially signaled, with automatic reset, and with
                // the specified security access. The Boolean value that 
                // indicates creation of the underlying system object
                // is placed in wasCreated.
                //
                ewh = new EventWaitHandle(
                    initialValue,
                    autoReset ? EventResetMode.AutoReset : EventResetMode.ManualReset,
                    eventName,
                    out wasCreated,
                    ewhSec);

                // If the named system event was created, it can be
                // used by the current instance of this program, even 
                // though the current user is denied access. The current
                // program owns the event. Otherwise, exit the program.
                // 
                if (wasCreated)
                {
                    return ewh;
                }
                else
                {
                    throw null;
                }
            }
            else if (unauthorized)
            {
                // Open the event to read and change the access control
                // security. The access control security defined above
                // allows the current user to do this.
                //
                ewh = EventWaitHandle.OpenExisting(eventName,
                    EventWaitHandleRights.ReadPermissions | EventWaitHandleRights.ChangePermissions);

                // Get the current ACL. This requires 
                // EventWaitHandleRights.ReadPermissions.
                EventWaitHandleSecurity ewhSec = ewh.GetAccessControl();

                string user = Environment.UserDomainName + "\\"
                    + Environment.UserName;

                // First, the rule that denied the current user 
                // the right to enter and release the event must
                // be removed.
                EventWaitHandleAccessRule rule =
                    new EventWaitHandleAccessRule(user,
                        EventWaitHandleRights.Synchronize | EventWaitHandleRights.Modify,
                        AccessControlType.Deny);
                ewhSec.RemoveAccessRule(rule);

                // Now grant the user the correct rights.
                // 
                rule = new EventWaitHandleAccessRule(user,
                    EventWaitHandleRights.Synchronize | EventWaitHandleRights.Modify,
                    AccessControlType.Allow);
                ewhSec.AddAccessRule(rule);

                // Update the ACL. This requires
                // EventWaitHandleRights.ChangePermissions.
                ewh.SetAccessControl(ewhSec);

                // Open the event with (EventWaitHandleRights.Synchronize 
                // | EventWaitHandleRights.Modify), the rights required
                // to wait on and signal the event.
                //
                ewh = EventWaitHandle.OpenExisting(eventName);

                return ewh;
            }

            return null;
        }
    }
}
