﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScanOutTool.Services
{
    public interface IAutoScanOutUI
    {
        string ReadPID();
        string ReadEBR();
        string ReadWO();
        string ReadResult();
        string ReadMessage();

        IntPtr GetMainHandle();

        (double X, double Y, double Width, double Height) GetResultElementBounds();
    }
}
