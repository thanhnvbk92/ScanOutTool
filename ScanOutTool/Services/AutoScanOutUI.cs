using ScanOutTool.Models;
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
        private string progressTextName = "txtProgress";

        private AutomationService automation;
        private bool isAttached = false;
        private string oldProgessbarString = "";
        private string oldMessage = "";
        private string oldResult = "";
        private string oldPID = "";

        private event Action<string> pIDChanged;



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
                string pid = automation.ReadTextByAutomationId(pidTextBoxName);
                if (pid != oldPID)

                {
                    oldPID = pid;
                    pIDChanged?.Invoke(pid);
                }
                return pid;
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

        private async Task<bool> WaitForGUIChangeAsync(int timeout = 5000)
        {
            int elapsed = 0;
            while (elapsed < timeout)
            {
                string currentPID = ReadPID();
                string currentProgress = automation.ReadTextByAutomationId(progressTextName);
                string currentMessage = ReadMessage();
                string currentResult = ReadResult();
                if (currentPID != oldPID)
                {
                    oldPID = currentPID;
                    return true;
                }
                if (currentProgress != oldProgessbarString)
                {
                    oldProgessbarString = currentProgress;
                    return true;
                }
                if (currentMessage != oldMessage)
                {
                    oldMessage = currentMessage;
                    return true;
                }
                await Task.Delay(100);
                elapsed += 100;
            }
            return false;
        }

        public async Task<PCB> ReadPCBInfor()
        {
            bool isChanged = await WaitForGUIChangeAsync();
            if (!isChanged)
            {
                return null;
            }
            PCB pCB = new PCB();
            pCB.PID = ReadPID();
            pCB.EBR = ReadEBR();
            pCB.WO = ReadWO();
            pCB.Result = ReadResult();
            pCB.Message = ReadMessage();
            return pCB;
        }
    }
}
