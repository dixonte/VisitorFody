using System;
using System.Collections.Generic;
using System.Text;
using Visitor;

namespace FodyVisitorTest.DomainModel
{
    [AcceptsVisitor(typeof(Visitors.IPersonVisitor))]
    public abstract class Person
    {
        public Guid Id { get; set; }
        public string Name { get; set; }

        //public virtual void Accept(Visitors.IPersonVisitor visitor)
        //{
        //    throw new NotImplementedException();
        //}
    }
}
