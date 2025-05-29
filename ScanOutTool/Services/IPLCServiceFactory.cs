using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScanOutTool.Services
{
    public interface IPLCServiceFactory
    {
        IPLCService Create(string ip, int port, bool isAscii);
    }

}
