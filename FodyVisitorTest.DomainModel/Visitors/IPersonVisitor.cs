using System;
using System.Collections.Generic;
using System.Text;

namespace FodyVisitorTest.DomainModel.Visitors
{
    public interface IPersonVisitor
    {
        void Visit(Person person);
        void Visit(Employee employee);
        void Visit(Director director);
    }
}
