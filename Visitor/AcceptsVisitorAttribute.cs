using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Visitor
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class AcceptsVisitorAttribute : Attribute
    {
        //public Type VisitorInterface { get; private set; }

        public AcceptsVisitorAttribute(Type visitorInterface)
        {
            if (!visitorInterface.IsInterface)
                throw new ArgumentException("VisitorInterface must be a Type of an Interface.");

            //VisitorInterface = visitorInterface;
        }
    }
}
