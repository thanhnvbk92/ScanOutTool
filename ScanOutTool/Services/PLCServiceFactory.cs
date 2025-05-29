using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScanOutTool.Services
{
    public class PLCServiceFactory : IPLCServiceFactory
    {
        public IPLCService Create(string ip, int port, bool isAscii)
        {
            return new PLCService(ip, port, isAscii);
        }
    }

}
