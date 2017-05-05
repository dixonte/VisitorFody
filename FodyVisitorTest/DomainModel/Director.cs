using System;
using System.Collections.Generic;
using System.Text;
using Visitor;

namespace FodyVisitorTest.DomainModel
{
    [AcceptsVisitor(typeof(Visitors.IPersonVisitor))]
    public class Director : Person
    {
        public string GolfPartner { get; set; }

        public void PlayGolf()
        {
            Console.Write("{0} plays golf", Name);

            if (!string.IsNullOrEmpty(GolfPartner))
                Console.Write(" with {0}", GolfPartner);

            Console.WriteLine(".");
        }

        public void DontPlayGolf()
        {
            Console.Write("{0} elects to not play golf", Name);

            if (!string.IsNullOrEmpty(this.GolfPartner))
                Console.Write(" with {0}", this.GolfPartner);

            Console.WriteLine(".");
        }

        //public override void Accept(Visitors.IPersonVisitor visitor)
        //{
        //    visitor.Visit(this);
        //}
    }
}
