using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FodyVisitorTest.DomainModel.Visitors
{
    public class DelegateDuck
    {
        public Action<Employee> Employee { get; set; }
        public Action<Director> Director { get; set; }

        public void Visit(Director director)
        {
            director.DontPlayGolf();
        }
    }
}
