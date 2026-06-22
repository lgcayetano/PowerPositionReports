using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PowerPositionReports
{
    public class PowerPosition
    {
        public int Period { get; set; }

        public string LocalTime { get; set; } = string.Empty;

        public double Volume { get; set; }
    }
}
