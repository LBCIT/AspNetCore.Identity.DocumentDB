﻿using Microsoft.Azure.Documents;
using Newtonsoft.Json;

namespace Microsoft.AspNetCore.Identity.DocumentDB
{
    /// <summary>
    /// A document used to map from another identifier to the partition key.
    /// In case of IdentityUser it maps from NormalizedUserName to UserId.
    /// In case of IdentityRole it maps from NormalizedName tod RoleId.
    /// The Id and PartitionKey are alqays equal here.
    /// </summary>
    internal class PartitionMapping : Document
    {
        // TODO make configurable
        [JsonProperty("partition")]
        public string PartitionKey { get { return Id; } }

        [JsonProperty("type")]
        public TypeEnum Type { get; set; }

        [JsonProperty("targetId")]
        public string TargetId { get; set; }
    }
}