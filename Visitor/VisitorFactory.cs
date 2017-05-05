using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Visitor
{
    public static class VisitorFactory<TVisitorInterface> where TVisitorInterface : class
    {
        public static TVisitorInterface Create<TVisitor>(TVisitor visitor, ActionOnMissing onMissing = ActionOnMissing.ThrowException) where TVisitor : class
        {
            throw new NotImplementedException();
        }
    }
}
