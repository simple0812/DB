using System.Runtime.InteropServices.ComTypes;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;


#if MSTEST
using Microsoft.VisualStudio.TestPlatform.UnitTestFramework;

using TestFixtureAttribute = Microsoft.VisualStudio.TestPlatform.UnitTestFramework.TestClassAttribute;
using SetUpAttribute = Microsoft.VisualStudio.TestPlatform.UnitTestFramework.TestInitializeAttribute;
using TestAttribute = Microsoft.VisualStudio.TestPlatform.UnitTestFramework.TestMethodAttribute;

#else
using NUnit.Framework;
#endif

namespace Velox.DB.Test
{
    [TestFixture]
    public class WithEmptyDB
    {
        private MyContext DB = MyContext.Instance;

        [SetUp]
        public void SetupTest()
        {
            DB.PurgeAll();
        }

        public WithEmptyDB()
        {
            DB.CreateAllTables();
        }

        [Test]
        public void Events_ObjectCreated()
        {
            int counter = 0;

            DB.Customers.Events.ObjectCreated += (sender, args) => { counter++; };
            DB.Customers.Events.ObjectCreated += (sender, args) => { counter++; };

            DB.Save(new Customer() {Name = "A"});
            DB.Save(new Customer() {Name = "A"});

            counter.Should().Be(4);
        }

        [Test]
        public void Events_ObjectCreating()
        {
            int counter = 0;

            EventHandler<Vx.ObjectWithCancelEventArgs<Customer>> ev1 = (sender, args) => { counter++; };
            EventHandler<Vx.ObjectWithCancelEventArgs<Customer>> ev2 = (sender, args) => { counter++; };

            DB.Customers.Events.ObjectCreating += ev1;
            DB.Customers.Events.ObjectCreating += ev2;

            try
            {
                bool saveResult = DB.Save(new Customer() {Name = "A"});

                DB.Customers.FirstOrDefault(c => c.Name == "A").Should().NotBeNull();

                saveResult.Should().Be(true);

                counter.Should().Be(2);
            }
            finally
            {
                DB.Customers.Events.ObjectCreating -= ev1;
                DB.Customers.Events.ObjectCreating -= ev2;
            }
        }

        [Test]
        public void Events_ObjectCreatingWithCancel1()
        {
            int counter = 0;

            EventHandler<Vx.ObjectWithCancelEventArgs<Customer>> ev = (sender, args) => { counter++; };
            EventHandler<Vx.ObjectWithCancelEventArgs<Customer>> evWithCancel = (sender, args) => { counter++; args.Cancel = true; };

            DB.Customers.Events.ObjectCreating += ev;
            DB.Customers.Events.ObjectCreating += evWithCancel;

            try
            {
                bool saveResult = DB.Save(new Customer() { Name = "A" });

                DB.Customers.FirstOrDefault(c => c.Name == "A").Should().BeNull();

                saveResult.Should().Be(false);

                counter.Should().Be(2);
            }
            finally
            {
                DB.Customers.Events.ObjectCreating -= ev;
                DB.Customers.Events.ObjectCreating -= evWithCancel;
            }
        }

        [Test]
        public void Events_ObjectCreatingWithCancel2()
        {
            int counter = 0;

            EventHandler<Vx.ObjectWithCancelEventArgs<Customer>> ev = (sender, args) => { counter++; };
            EventHandler<Vx.ObjectWithCancelEventArgs<Customer>> evWithCancel = (sender, args) => { counter++; args.Cancel = true; };

            DB.Customers.Events.ObjectCreating += evWithCancel;
            DB.Customers.Events.ObjectCreating += ev;

            try
            {
                bool saveResult = DB.Save(new Customer() { Name = "A" });

                DB.Customers.FirstOrDefault(c => c.Name == "A").Should().BeNull();

                saveResult.Should().Be(false);

                counter.Should().Be(1);
            }
            finally
            {
                DB.Customers.Events.ObjectCreating -= ev;
                DB.Customers.Events.ObjectCreating -= evWithCancel;
            }
        }


        [Test]
        public void ManyToOne()
        {
            Customer customer = new Customer { Name = "x" };

            customer.Save();

            SalesPerson salesPerson = new SalesPerson {Name = "Test"};

            salesPerson.Save();

            var order = new Order
            {
                SalesPersonID = null,
                CustomerID = customer.CustomerID
            };

            DB.Orders.Insert(order);

            int id = order.OrderID;

            order = DB.Orders.Read(id, o => o.Customer);

            Assert.AreEqual(order.Customer.CustomerID, customer.CustomerID);

            order.SalesPersonID = salesPerson.ID;
            order.Save();

            order = DB.Orders.Read(id, (o) => o.SalesPerson);

            Assert.AreEqual(salesPerson.ID, order.SalesPerson.ID);

            order.SalesPersonID = null;
            order.SalesPerson = null;
            order.Save();

            order = DB.Orders.Read(id, o => o.SalesPerson);

            Assert.IsNull(order.SalesPerson);
            Assert.IsNull(order.SalesPersonID);
        }

        [Test]
        public void ReverseRelation_Generic()
        {
            Order order = new Order()
            {
                Customer = new Customer() {Name = "A"},
                OrderItems = new List<OrderItem>()
                {
                    new OrderItem() {Description = "X"},
                    new OrderItem() {Description = "X"},
                    new OrderItem() {Description = "X"},
                    new OrderItem() {Description = "X"},
                    new OrderItem() {Description = "X"},
                }
            };

            var originalOrder = order;

            DB.Orders.Insert(order, true);

            order = DB.Orders.Read(originalOrder.OrderID);

            Vx.LoadRelations(order, o => o.OrderItems);

            order.OrderItems.Should().HaveCount(5).And.OnlyContain(item => item.Order == order);


        }

        [Test]
        public void ReverseRelation_DataSet()
        {
            Customer customer = new Customer() {Name = "A"};

            DB.Customers.Insert(customer);

            for (int i = 0; i < 5; i++)
                DB.Orders.Insert(new Order()
                {
                    CustomerID = customer.CustomerID
                });

            customer = DB.Customers.Read(customer.CustomerID);

            customer.Orders.Should().HaveCount(5).And.OnlyContain(order => order.Customer == customer);


        }

        [Test]
        public void ReverseRelation_OneToOne()
        {
            OneToOneRec1 rec1 = new OneToOneRec1();
            OneToOneRec2 rec2 = new OneToOneRec2();

            DB.Insert(rec1);
            DB.Insert(rec2);

            rec1.OneToOneRec2ID = rec2.OneToOneRec2ID;
            rec2.OneToOneRec1ID = rec1.OneToOneRec1ID;

            DB.Update(rec1);
            DB.Update(rec2);

            rec1 = DB.Read<OneToOneRec1>(rec1.OneToOneRec1ID, r=> r.Rec2 );

            rec1.Rec2.Rec1.Should().Be(rec1);

        }

        [Test]
        public void OneToManyWithOptionalRelation()
        {
            Customer customer = new Customer { Name = "x" };

            customer.Save();

            SalesPerson salesPerson = new SalesPerson { Name = "Test" };

            salesPerson.Save();

            Order[] orders = new[]
            {
                new Order() { CustomerID = customer.CustomerID, OrderDate = DateTime.Today, SalesPersonID = null},
                new Order() { CustomerID = customer.CustomerID, OrderDate = DateTime.Today, SalesPersonID = salesPerson.ID}
            };

            foreach (var order in orders)
            {
                DB.Insert(order);
            }

            salesPerson = DB.SalesPeople.First();

            salesPerson.Orders.Count().Should().Be(1);
            salesPerson.Orders.First().OrderID.Should().Be(orders[1].OrderID);
            



        }

        [Test]
        public void AsyncInsert()
        {
            const int numThreads = 100;

            List<string> failedList = new List<string>();
            Task<bool>[] saveTasks = new Task<bool>[numThreads];
            Customer[] customers = new Customer[numThreads];
            List<Customer> createdCustomers = new List<Customer>();

            HashSet<int> ids = new HashSet<int>();

            for (int i = 0; i < numThreads; i++)
            {
                string name = "C" + (i + 1);

                Customer customer = new Customer { Name = name };

                customers[i] = customer;
                saveTasks[i] = DB.Customers.Async().Insert(customer);

                saveTasks[i].ContinueWith(t =>
                {
                    if (customer.CustomerID == 0)
                        lock (failedList)
                            failedList.Add("CustomerID == 0");

                    lock (ids)
                    {
                        if (ids.Contains(customer.CustomerID))
                            failedList.Add("Dupicate CustomerID " + customer.CustomerID + " for " + customer.Name);

                        ids.Add(customer.CustomerID);
                    }

                    lock (createdCustomers)
                        createdCustomers.Add(customer);

                    DB.Customers.Async().Read(customer.CustomerID).ContinueWith(tRead =>
                    {
                        if (customer.Name != tRead.Result.Name)
                            lock (failedList)
                                failedList.Add(string.Format("Customer == ({0},{1})" + ", but should be ({2},{3})", tRead.Result.CustomerID, tRead.Result.Name, customer.CustomerID, customer.Name));
                    });
                });
            }


            Task.WaitAll(saveTasks);

            saveTasks.Should().NotContain(t => t.IsFaulted);

            createdCustomers.Count.Should().Be(numThreads);

            foreach (var fail in failedList)
            {
                Assert.Fail(fail);
            }
        }


        [Test]
        public void ParallelTest1()
        {
            const int numThreads = 100;

            Task[] tasks = new Task[numThreads];

            List<string> failedList = new List<string>();
            Customer[] customers = new Customer[numThreads];
            List<Customer> createdCustomers = new List<Customer>();

            HashSet<int> ids = new HashSet<int>();

            for (int i = 0; i < numThreads; i++)
            {
                string name = "C" + (i + 1);

                tasks[i] = Task.Factory.StartNew(() =>
                {
                    Customer customer = new Customer { Name = name };

                    customer.Save();

                    if (customer.CustomerID == 0)
                        lock (failedList)
                            failedList.Add("CustomerID == 0");

                    lock (ids)
                    {
                        if (ids.Contains(customer.CustomerID))
                            failedList.Add("Dupicate CustomerID " + customer.CustomerID + " for " + customer.Name);

                        ids.Add(customer.CustomerID);
                    }

                    lock (createdCustomers)
                        createdCustomers.Add(customer);

                    var newCustomer = Vx.DataSet<Customer>().Read(customer.CustomerID);

                    if (customer.Name != newCustomer.Name)
                        lock (failedList)
                            failedList.Add(string.Format("Customer == ({0},{1})" + ", but should be ({2},{3})", newCustomer.CustomerID, newCustomer.Name, customer.CustomerID, customer.Name));

                });
            }

            foreach (var task in tasks)
            {
                task.Wait();
            }

            foreach (var fail in failedList)
            {
                Assert.Fail(fail);
            }

            createdCustomers.Count.Should().Be(numThreads);
        }

        private void CreateRandomPricedProducts()
        {
            Random rnd = new Random();

            var products = Enumerable.Range(1, 20).Select(i => new Product() { ProductID = "P" + i, Description = "Product " + i, Price = (decimal)(rnd.NextDouble() * 100), MinQty = 1 });

            foreach (var product in products)
                DB.Products.Insert(product);


        }

        [Test]
        public void StartsWith()
        {
            var products = Enumerable.Range(1, 20).Select(i => new Product()
            {
                ProductID = "P" + i, Description = (char)('A'+(i%10)) + "-Product", Price = 0.0m, MinQty = 1
            });

            foreach (var product in products)
                DB.Products.Insert(product);

            var pr = (from p in DB.Products where p.Description.StartsWith("B") select p).ToArray();

            pr.Count().Should().Be(2);
            pr.All(p => p.Description.StartsWith("B")).Should().BeTrue();

        }

        [Test]
        public void EndsWith()
        {
            var products = Enumerable.Range(1, 20).Select(i => new Product()
            {
                ProductID = "P" + i,
                Description = "Product-"+(char)('A' + (i % 10)),
                Price = 0.0m,
                MinQty = 1
            });

            foreach (var product in products)
                DB.Products.Insert(product);

            var pr = (from p in DB.Products where p.Description.EndsWith("B") select p).ToArray();

            pr.Count().Should().Be(2);
            pr.All(p => p.Description.EndsWith("B")).Should().BeTrue();
        }


        [Test]
        public void SortNumeric_Linq()
        {
            CreateRandomPricedProducts();

            var sortedProducts = from product in DB.Products orderby product.Price select product;

            sortedProducts.Should().BeInAscendingOrder(product => product.Price);

            sortedProducts = from product in DB.Products orderby product.Price descending select product;

            sortedProducts.Should().BeInDescendingOrder(product => product.Price);
        }

        [Test]
        public void CreateAndReadSingleObject()
        {
            Customer customer = new Customer { Name = "A" };

            customer.Save();

            Assert.IsTrue(customer.CustomerID > 0);

            customer = DB.Customers.Read(customer.CustomerID);

            Assert.AreEqual("A",customer.Name);
        }

        [Test]
        public void CreateAndUpdateSingleObject()
        {
            Customer customer = new Customer { Name = "A" };

            customer.Save();

            customer = DB.Customers.Read(customer.CustomerID);

            customer.Name = "B";
            customer.Save();

            customer = DB.Customers.Read(customer.CustomerID);

            Assert.AreEqual("B",customer.Name);
        }

        [Test]
        public void ReadNonexistantObject()
        {
            Customer customer = DB.Customers.Read(70000);

            Assert.IsNull(customer);
        }



        [Test]
        public void CreateWithRelation_ManyToOne_ByID()
        {
            Customer customer = new Customer { Name = "A" };

            customer.Save();

            var order = new Order
            {
                Remark = "test",
                CustomerID = customer.CustomerID
            };

            Assert.IsTrue(order.Save());

            Order order2 = DB.Orders.Read(order.OrderID, o => o.Customer);

            Vx.LoadRelations(() => order.Customer);

            order2.Customer.Name.Should().Be(order.Customer.Name);
            order2.Customer.CustomerID.Should().Be(order.Customer.CustomerID);
            order2.Customer.CustomerID.Should().Be(order.CustomerID);
        }

        [Test]
        public void CreateWithRelation_ManyToOne_ByRelationObject()
        {
            Customer customer = new Customer() { Name = "me" };

            customer.Save();

            var order = new Order
            {
                Remark = "test",
                Customer = customer
            };

            Assert.IsTrue(order.Save());

            Order order2 = DB.Orders.Read(order.OrderID, o => o.Customer);

            Vx.LoadRelations(() => order.Customer);

            Assert.AreEqual(order2.Customer.Name, order.Customer.Name);
            Assert.AreEqual(order2.Customer.CustomerID, order.Customer.CustomerID);
            Assert.AreEqual(order2.Customer.CustomerID, order.CustomerID);
        }

        [Test]
        public void CreateWithRelation_ManyToOne_ByRelationObject_New()
        {
            Customer customer = new Customer() { Name = "me" };

            var order = new Order
            {
                Remark = "test",
                Customer = customer
            };

            Assert.IsTrue(order.Save(true));

            Order order2 = DB.Orders.Read(order.OrderID, o => o.Customer);

            Vx.LoadRelations(() => order.Customer);

            Assert.AreEqual(order2.Customer.Name, order.Customer.Name);
            Assert.AreEqual(order2.Customer.CustomerID, order.Customer.CustomerID);
            Assert.AreEqual(order2.Customer.CustomerID, order.CustomerID);
        }


        [Test]
        public void CreateOrderWithNewCustomer()
        {
            Customer customer = new Customer() {Name = "me"};

            customer.Save();

            var order = new Order
            {
                Remark = "test", 
                CustomerID = customer.CustomerID
            };

            Assert.IsTrue(order.Save());

            Vx.LoadRelations(() => order.Customer);

            Order order2 = DB.Orders.Read(order.OrderID , o => o.Customer);

            Assert.AreEqual(order2.Customer.Name, order.Customer.Name);
            Assert.AreEqual(order2.Customer.CustomerID, order.Customer.CustomerID);
            Assert.AreEqual(order2.Customer.CustomerID, order.CustomerID);

            Vx.LoadRelations(() => order2.Customer.Orders);

            Assert.AreEqual(order2.Customer.Orders.First().CustomerID, order.CustomerID);
        }

        [Test]
        public void CreateOrderWithExistingCustomer()
        {
            Customer cust = new Customer { Name = "A" };

            cust.Save();

            cust = DB.Customers.Read(cust.CustomerID);

            Order order = new Order();

            order.CustomerID = cust.CustomerID;

            Assert.IsTrue(order.Save());

            order = DB.Orders.Read(order.OrderID);

            Vx.LoadRelations(() => order.Customer);
            Vx.LoadRelations(() => order.Customer.Orders);

            Assert.AreEqual(order.Customer.Name, cust.Name);
            Assert.AreEqual(order.Customer.CustomerID, cust.CustomerID);
            Assert.AreEqual(order.CustomerID, cust.CustomerID);

            Assert.AreEqual((order.Customer.Orders.First()).CustomerID, cust.CustomerID);

            order.Customer.Name = "B";
            order.Customer.Save();


            order = DB.Orders.Read(order.OrderID);

            Vx.LoadRelations(() => order.Customer);

            Assert.AreEqual(order.CustomerID, cust.CustomerID);

            Assert.AreEqual("B", order.Customer.Name);
        }

        [Test]
        public void DeleteSingleObject()
        {
            List<Customer> customers = new List<Customer>();

            for (int i = 0; i < 10; i++)
            {
                Customer customer = new Customer() {Name = "Customer " + (i + 1)};

                customer.Save();

                customers.Add(customer);
            }

            DB.Customers.Delete(customers[5]);

            Assert.IsNull(DB.Customers.Read(customers[5].CustomerID));

            Assert.AreEqual(9,DB.Customers.Count());
        }

        [Test]
        public void DeleteMultipleObjects()
        {
            List<Customer> customers = new List<Customer>();

            for (int i = 0; i < 10; i++)
            {
                Customer customer = new Customer() { Name = "Customer " + (i + 1) };

                customer.Save();

                customers.Add(customer);
            }

            DB.Customers.Delete(c => c.Name == "Customer 2" || c.Name == "Customer 4");

            Assert.IsNotNull(DB.Customers.Read(customers[0].CustomerID));
            Assert.IsNull(DB.Customers.Read(customers[1].CustomerID));
            Assert.IsNotNull(DB.Customers.Read(customers[2].CustomerID));
            Assert.IsNull(DB.Customers.Read(customers[3].CustomerID));

            Assert.AreEqual(8, DB.Customers.Count());
        }

        [Test]
        public void CreateOrderWithNewItems()
        {
            Order order = new Order
            {
                Customer = new Customer
                {
                    Name = "test"
                },
                OrderItems = new List<OrderItem>
                {
                    new OrderItem {Description = "test", Qty = 5, Price = 200.0},
                    new OrderItem {Description = "test", Qty = 3, Price = 45.0}
                }
            };

            Assert.IsTrue(order.Save(true));

            order = DB.Orders.Read(order.OrderID, o => o.OrderItems);

            //double totalPrice = Convert.ToDouble(order.OrderItems.GetScalar("Qty * Price", CSAggregate.Sum));

            Assert.AreEqual(2, order.OrderItems.Count, "Order items not added");
            //Assert.AreEqual(1135.0, totalPrice, "Incorrect total amount");

            order.OrderItems.Add(new OrderItem { Description = "test", Qty = 2, Price = 1000.0 });

            Assert.IsTrue(order.Save(true));

            order = DB.Orders.Read(order.OrderID, o => o.OrderItems);

            //totalPrice = Convert.ToDouble(order.OrderItems.GetScalar("Qty * Price", CSAggregate.Sum));

            Assert.AreEqual(3, order.OrderItems.Count, "Order item not added");
            //Assert.AreEqual(3135.0, totalPrice, "Total price incorrect");

            /*
            order.OrderItems.DeleteAll();

            order = Order.Read(order.OrderID);

            Assert.AreEqual(0, order.OrderItems.Count, "Order items not deleted");

            Assert.IsTrue(order.Delete());
                */

        }

        [Test]
        public void RandomCreation()
        {
            Random rnd = new Random();

            Customer cust = new Customer();
            cust.Name = "A";
            cust.Save();

            double total = 0.0;

            for (int i = 0; i < 5; i++)
            {
                Order order = new Order
                {
                    Customer = cust
                };


                order.Save();

                for (int j = 0; j < 20; j++)
                {
                    int qty = rnd.Next(1, 10);
                    double price = rnd.NextDouble() * 500.0;

                    OrderItem item = new OrderItem() { Description = "test", Qty = (short)qty, Price = price, OrderID = order.OrderID };

                    item.Save();

                    total += qty * price;
                }


            }



            var orders = DB.Orders.ToArray();

            Assert.AreEqual(5, orders.Length);

            double total2 = DB.OrderItems.Sum(item => item.Qty*item.Price);

            Assert.AreEqual(total, total2, 0.000001);

            foreach (Order order in orders)
            {
                Vx.LoadRelations(order, o => o.Customer, o => o.OrderItems);

                Assert.AreEqual(cust.CustomerID, order.Customer.CustomerID);
                Assert.AreEqual(20, order.OrderItems.Count);
                Assert.AreEqual(cust.Name, order.Customer.Name);

                DB.OrderItems.Delete(order.OrderItems.First());
            }

            total2 = DB.OrderItems.Sum(item => item.Qty * item.Price);

            total.Should().BeGreaterThan(total2);

            Assert.AreEqual(95, DB.OrderItems.Count());
        }


        [Test]
        public void CompositeKeyCreateAndRead()
        {
            DB.Insert(new RecordWithCompositeKey
            {
                Key1 = 1, 
                Key2 = 2, 
                Name = "John"
            });

            var rec = DB.Read<RecordWithCompositeKey>(new {Key1 = 1, Key2 = 2});

            rec.Should().NotBeNull();
            rec.Key1.Should().Be(1);
            rec.Key2.Should().Be(2);
            rec.Name.Should().Be("John");
        }
    }
}