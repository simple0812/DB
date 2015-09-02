using System.Reflection;

#if VELOX_DB
namespace Velox.DB.Core
#else
namespace Velox.Core
#endif
{
    public class PropertyInspector : MemberInspector
    {
        private readonly PropertyInfo _propertyInfo;

        public PropertyInspector(PropertyInfo propertyInfo) : base(propertyInfo)
        {
            _propertyInfo = propertyInfo;
        }

        public bool CanRead => _propertyInfo.CanRead;
        public new bool CanWrite => _propertyInfo.CanWrite;
    }
}