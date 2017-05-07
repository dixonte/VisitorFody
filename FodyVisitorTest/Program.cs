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

            // Since ConcreteVisitor already implements IPersonVisitor, these statements should generate the same IL
            var concreteVisitor = VisitorFactory<IPersonVisitor>.Create(new ConcreteVisitor());
            concreteVisitor = new ConcreteVisitor();

            // Anonymous classes provide their implementations by way of Action<> delegates
            // The name of the properties is not important
            var anonymousVisitor = VisitorFactory<IPersonVisitor>.Create(new
            {
                Employee = new Action<Employee>(e => employeeCountByVisitor++),
                Director = new Action<Director>(d =>
                {
                    d.PlayGolf();
                })
            }, ActionOnMissing.NoOp);

            // Concrete classes can also provide implementations by way of Action<> delegates
            // Use caution, as any properties of type Action<> that match a Visit method in the given interface will be wired up as implementation
            var concreteDelegate = VisitorFactory<IPersonVisitor>.Create(new ConcreteDelegate()
            {
                Employee = new Action<Employee>(e => employeeCountByVisitor++)
            });

            // If for some reason you want to, you can use VisitorFactory to inject visitor interfaces into concrete classes with methods
            // At the moment, this will take the first method that matches the required signature, regardless of name.
            // This will probably change in future.
            var concreteDuck = VisitorFactory<IPersonVisitor>.Create(new ConcreteDuck());

            // In the case where a concrete class has a method and a Action<> delegate that both match, the method trumps the delegate
            var delegateDuck = VisitorFactory<IPersonVisitor>.Create(new DelegateDuck()
            {
                Employee = new Action<Employee>(e => employeeCountByVisitor++),
                Director = new Action<Director>(director =>
                {
                    director.PlayGolf();
                })
            });

            foreach (var person in people)
            {
                person.Accept(anonymousVisitor);
            }

            Console.WriteLine($"There are {employeeCountByVisitor} employees.");
            Console.Read();
        }
    }
}
