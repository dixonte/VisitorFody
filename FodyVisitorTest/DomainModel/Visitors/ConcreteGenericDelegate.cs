using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FodyVisitorTest.DomainModel.Visitors
{
    public class ConcreteGenericDelegate<T>
    {
        public ConcreteGenericDelegate(T maybeAction)
        {
            MaybeAction = maybeAction;
        }

        public T MaybeAction { get; set; }

        public void Vart(Director d)
        {
            (MaybeAction as Action<Director>)?.Invoke(d);
        }
    }
}
