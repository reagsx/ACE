﻿using System;
using System.Collections.Generic;

namespace ACE.Database.Models.Shard
{
    public partial class BiotaPropertiesEnchantmentRegistry
    {
        public uint Id { get; set; }
        public uint EnchantmentCategory { get; set; }
        public uint ObjectId { get; set; }
        public int SpellId { get; set; }
        public ushort LayerId { get; set; }
        public bool HasSpellSetId { get; set; }
        public ushort SpellCategory { get; set; }
        public uint PowerLevel { get; set; }
        public double StartTime { get; set; }
        public double Duration { get; set; }
        public uint CasterObjectId { get; set; }
        public float DegradeModifier { get; set; }
        public float DegradeLimit { get; set; }
        public double LastTimeDegraded { get; set; }
        public uint StatModType { get; set; }
        public uint StatModKey { get; set; }
        public float StatModValue { get; set; }
        public uint SpellSetId { get; set; }

        public Biota Object { get; set; }
    }
}
