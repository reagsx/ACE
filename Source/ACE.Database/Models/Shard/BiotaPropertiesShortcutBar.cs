﻿using System;
using System.Collections.Generic;

namespace ACE.Database.Models.Shard
{
    public partial class BiotaPropertiesShortcutBar
    {
        public uint Id { get; set; }
        public uint ObjectId { get; set; }
        public uint ShortcutBarIndex { get; set; }
        public uint ShortcutObjectId { get; set; }

        public Biota Object { get; set; }
        public Biota ShortcutObject { get; set; }
    }
}
