using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UIAutoLib.Services;

namespace ScanOutTool.Services
{
    public class AutoScanOutUI : IAutoScanOutUI
    {
        private string processName = "LGE.SFC.MainFrame";
        private string pidTextBoxName = "txtSerialNo";
        private string ebrTextBoxName = "txtModelSuffix";
        private string woTextBoxName = "txtWorkOrder";
        private string resultTextBoxName = "txtResult";
        private string messageTextBoxName = "txtMessage";
        private AutomationService automation;
        private bool isAttached = false;



        public AutoScanOutUI()
        {
            automation = new AutomationService();
            isAttached = automation.AttachToProcess(processName);
            if (!isAttached)
            {
                throw new Exception($"Failed to attach to the process. Please open App {processName}");
            }
        }

        public string ReadPID()
        {
            if (isAttached)
            {
                return automation.ReadTextByAutomationId(pidTextBoxName);
            }
            else
            {
                throw new Exception("Failed to attach to the process.");
            }
        }

        public string ReadEBR()
        {
            if (isAttached)
            {
                return automation.ReadTextByAutomationId(ebrTextBoxName).TrimEnd('.');
            }
            else
            {
                throw new Exception("Failed to attach to the process.");
            }
        }

        public string ReadWO()
        {
            if (isAttached)
            {
                return automation.ReadTextByAutomationId(woTextBoxName);
            }
            else
            {
                throw new Exception("Failed to attach to the process.");
            }
        }

        public string ReadResult()
        {
            if (isAttached)
            {
                return automation.ReadTextByAutomationId(resultTextBoxName);
            }
            else
            {
                throw new Exception("Failed to attach to the process.");
            }
        }

        public string ReadMessage()
        {
            if (isAttached)
            {
                return automation.ReadTextByAutomationId(messageTextBoxName);
            }
            else
            {
                throw new Exception("Failed to attach to the process.");
            }
        }

        public (double X, double Y, double Width, double Height) GetResultElementBounds()
        {
            if (isAttached)
            {
                return automation.GetElementBounds(resultTextBoxName);
            }
            else
            {
                throw new Exception("Failed to attach to the process.");
            }
        }

        public IntPtr GetMainHandle()
        {
            if (isAttached)
            {
                return automation.GetMainHandle();
            }
            else
            {
                throw new Exception("Failed to attach to the process.");
            }
        }
    }
}
