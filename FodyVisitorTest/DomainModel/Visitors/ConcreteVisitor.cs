using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FodyVisitorTest.DomainModel.Visitors
{
    public class ConcreteVisitor : IPersonVisitor
    {
        void IPersonVisitor.Visit(Person person)
        {
        }

        void IPersonVisitor.Visit(Employee employee)
        {
        }

        void IPersonVisitor.Visit(Director director)
        {
        }
    }
}
