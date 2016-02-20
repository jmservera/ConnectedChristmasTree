using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shared
{
    public static class Logger
    {
        public static event EventHandler<string> MessageReceived;
        public static void Log(string message, [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
        {
            System.Diagnostics.Debug.WriteLine($"{caller}: {message}");
            if (MessageReceived != null)
            {
                MessageReceived(caller, message);
            }
        }
    }
}
