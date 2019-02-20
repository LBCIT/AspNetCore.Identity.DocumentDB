using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Runtime.Serialization;

namespace Microsoft.AspNetCore.Identity.DocumentDB
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum TypeEnum
    {
        [EnumMember(Value = "user")]
        User,

        [EnumMember(Value = "userMappingUsername")]
        UserMappingUsername,

        [EnumMember(Value = "userMappingEmail")]
        UserMappingEmail,

        [EnumMember(Value = "role")]
        Role,

        [EnumMember(Value = "roleMapping")]
        RoleMapping
    }
}
