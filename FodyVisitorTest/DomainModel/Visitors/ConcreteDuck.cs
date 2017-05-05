using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FodyVisitorTest.DomainModel.Visitors
{
    public class ConcreteDuck
    {
        public void Visit(Employee employee)
        {
            employee.Work(new Random((int)DateTime.Now.Ticks).Next(1, 8));
        }

        public void Visit(Director director)
        {
            director.DontPlayGolf();
        }
    }
}
