using System;
using System.Collections.Generic;
using System.Text;
using Visitor;

namespace FodyVisitorTest.DomainModel
{
    [AcceptsVisitor(typeof(Visitors.IPersonVisitor))]
    public class Employee : Person
    {
        public void Work(int hours)
        {
            Console.WriteLine("{0} works for {1} hours.", Name, hours);
        }

        //public override void Accept(Visitors.IPersonVisitor visitor)
        //{
        //    visitor.Visit(this);
        //}
    }
}
