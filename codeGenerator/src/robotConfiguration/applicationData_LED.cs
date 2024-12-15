using DataConfiguration;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace ApplicationData
{
    [Serializable()]
    [UserIncludeFile("hw/DragonLeds.h")]
    public class Led : baseRobotElementClass
    {
        [DefaultValue(0u)]
        [Range(typeof(uint), "0", "9")]
        public uintParameter PwmId { get; set; }

        public List<LedSegment> Segments { get; set; }

        public Led()
        {
        }

        public override string getDisplayName(string propertyName, out helperFunctions.RefreshLevel refresh)
        {
            refresh = helperFunctions.RefreshLevel.none;
            return "LEDs";
        }
    }

    [Serializable()]
    public class LedSegment : baseRobotElementClass
    {
        [DefaultValue(0u)]
        public uintParameter Count { get; set; }

        public LedSegment()
        {
        }
    }
}
