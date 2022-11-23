﻿// -----------------------------------------------------------------------
//  <copyright file="CompoundOrQueryWithOrderByDescending .cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using FastTests;
using FastTests.Server.Documents.Indexing;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Session;
using Xunit;
using Xunit.Abstractions;
using Tests.Infrastructure;

namespace SlowTests.MailingList
{
    public class CompoundOrQueryWithOrderByDescending : RavenTestBase
    {
        public CompoundOrQueryWithOrderByDescending(ITestOutputHelper output) : base(output)
        {
        }

        [Theory]
        [RavenData]
        public void ThreeOrClauses_works(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                store.Initialize();

                var itemList = new List<Item> { new Item("A") };

                using (IDocumentSession s = store.OpenSession())
                {
                    s.Store(new MyDoc
                    {
                        Foo = "monkey",
                        ItemListOne = itemList,
                        ItemListTwo = itemList,
                        ItemListThree = itemList,
                        ItemListFour = itemList
                    });
                    s.Store(new MyDoc
                    {
                        Foo = "monkey",
                        ItemListOne = itemList,
                        ItemListTwo = itemList,
                        ItemListThree = itemList,
                        ItemListFour = itemList
                    });
                    s.SaveChanges();
                }
                using (IDocumentSession s = store.OpenSession())
                {
                    MyDoc[] res = s.Query<MyDoc>()
                        .Where(x => x.Foo == "monkey" && (
                                        x.ItemListOne.Any(i => i.MyProp == "A") ||
                                        x.ItemListTwo.Any(i => i.MyProp == "A") ||
                                        x.ItemListThree.Any(i => i.MyProp == "A")))
                        .OrderByDescending(x => x.CreatedDate)
                        .ToArray();

                    Assert.True(res.Count() == 2);
                }
            }
        }


        [Theory]
        [RavenData]
        public void FourOrClauses_fails(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                store.Initialize();

                var itemList = new List<Item> { new Item("A") };

                using (IDocumentSession s = store.OpenSession())
                {
                    s.Store(new MyDoc
                    {
                        Foo = "monkey",
                        ItemListOne = itemList,
                        ItemListTwo = itemList,
                        ItemListThree = itemList,
                        ItemListFour = itemList
                    });
                    s.Store(new MyDoc
                    {
                        Foo = "monkey",
                        ItemListOne = itemList,
                        ItemListTwo = itemList,
                        ItemListThree = itemList,
                        ItemListFour = itemList
                    });
                    s.SaveChanges();
                }
                using (IDocumentSession s = store.OpenSession())
                {
                    MyDoc[] res = s.Query<MyDoc>()
                        .Where(x => x.Foo == "monkey" && (
                                        x.ItemListOne.Any(i => i.MyProp == "A") ||
                                        x.ItemListTwo.Any(i => i.MyProp == "A") ||
                                        x.ItemListThree.Any(i => i.MyProp == "A") ||
                                        x.ItemListFour.Any(i => i.MyProp == "A")))
                        //Additional Or clause
                        .OrderByDescending(x => x.CreatedDate)
                        .ToArray();

                    Assert.True(res.Count() == 2);
                }
            }
        }


        private class Item
        {
            public Item(string propvalue)
            {
                MyProp = propvalue;
            }

            public string MyProp { get; set; }
        }

        private class MyDoc
        {
            public MyDoc()
            {
                CreatedDate = DateTime.Now;
            }

            public string Foo { get; set; }

            public DateTime CreatedDate { get; set; }

            public List<Item> ItemListOne { get; set; }

            public List<Item> ItemListTwo { get; set; }

            public List<Item> ItemListThree { get; set; }

            public List<Item> ItemListFour { get; set; }
        }
    }
}
