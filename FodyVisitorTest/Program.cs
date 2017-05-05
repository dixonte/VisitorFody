using FodyVisitorTest.DomainModel;
using FodyVisitorTest.DomainModel.Visitors;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Visitor;

namespace FodyVisitorTest
{
    class Program
    {
        static void Main(string[] args)
        {
            var people = new List<Person>();

            people.Add(new Employee() { Name = "Frank" });
            people.Add(new Employee() { Name = "Rob" });
            people.Add(new Employee() { Name = "Joe" });
            people.Add(new Director() { Name = "Andy" });
            people.Add(new Director() { Name = "Barry", GolfPartner = "Fabio" });
            people.Add(new Employee() { Name = "Stuart" });


            int employeeCountByVisitor = 0;

            var anonymousVisitor = VisitorFactory<IPersonVisitor>.Create(new {
                Employee = new Action<Employee>(e => employeeCountByVisitor++),
                Director = new Action<Director>(d =>
                {
                    d.PlayGolf();
                })
            }, ActionOnMissing.NoOp);
            var concreteVisitor = VisitorFactory<IPersonVisitor>.Create(new ConcreteVisitor());
            var concreteDelegate = VisitorFactory<IPersonVisitor>.Create(new ConcreteDelegate());
            var concreteDuck = VisitorFactory<IPersonVisitor>.Create(new ConcreteDuck());
            concreteVisitor = new ConcreteVisitor();

            foreach (var person in people)
            {
                person.Accept(concreteDuck);
            }

            Console.WriteLine($"There are {employeeCountByVisitor} employees.");
            Console.Read();
        }
    }
}
