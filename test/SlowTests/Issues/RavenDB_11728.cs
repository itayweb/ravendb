﻿using System.Collections.Generic;
using System.Linq;
using FastTests;
using Raven.Client.Documents.Indexes;
using Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace SlowTests.Issues
{
    public class RavenDB_11728 : RavenTestBase
    {
        public RavenDB_11728(ITestOutputHelper output) : base(output)
        {
        }

        [Theory]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.All)]
        public void MustReturnOneResult(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                new EmailSequenceWithStatusIndex().Execute(store);

                using (var session = store.OpenSession())
                {
                    session.Store(new EmailMessage()
                    {
                        Id = "EmailMessages/1",
                        Status = EmailProcessingStatus.Queued
                    });

                    session.Store(new EmailMessage()
                    {
                        Id = "EmailMessages/2",
                        Status = EmailProcessingStatus.Sent
                    });

                    session.Store(new EmailSequence()
                    {
                        EmailsIds = new List<string>()
                        {
                            "EmailMessages/1",
                            "EmailMessages/2"
                        },
                        Parameters = new EmailSequenceSendoutParameters()
                        {
                            RecipientAddress = "a",
                            RecipientDisplay = "a",
                            Data = new Dictionary<string, string>()
                        }
                    });

                    session.SaveChanges();
                    WaitForUserToContinueTheTest(store);
                    var results = session.Query<EmailSequenceWithStatusIndex.Result, EmailSequenceWithStatusIndex>().Customize(x => x.WaitForNonStaleResults()).ToList();
                    WaitForUserToContinueTheTest(store);                    
                    Assert.Equal(1, results.Count);
                }
            }
        }

        private class EmailSequenceWithStatusIndex : AbstractIndexCreationTask<EmailSequence, EmailSequenceWithStatusIndex.Result>
        {
            public class Result
            {
                public class EmailIdWithStatus
                {
                    public string Id { get; set; }

                    public EmailProcessingStatus? Status { get; set; }
                }
                public EmailSequenceSendoutParameters Parameters { get; set; }

                public EmailIdWithStatus[] Emails { get; set; }
            }

            public EmailSequenceWithStatusIndex()
            {
                Map = sequences =>
                    from seq in sequences
                    from emailId in seq.EmailsIds
                    let email = LoadDocument<EmailMessage>(emailId)
                    select new Result
                    {
                        Parameters = seq.Parameters,
                        Emails = new[]
                        {
                            new Result.EmailIdWithStatus
                            {
                                Id = emailId,
                                Status = email.Status
                            }
                        }
                    };

                Reduce = results =>
                    from result in results
                    group result by new
                    {
                        result.Parameters
                    }
                    into g
                    let key = g.Key
                    select new Result
                    {
                        Parameters = key.Parameters,
                        Emails = g.SelectMany(x => x.Emails).ToArray()
                    };
                
                Stores.Add(i => i.Parameters, FieldStorage.Yes);
                Index(i => i.Parameters, FieldIndexing.No);
                Stores.Add(i => i.Emails, FieldStorage.Yes);
                Index(i => i.Emails, FieldIndexing.No);  
            }
        }

        private class EmailSequence
        {
            public string Id { get; set; }

            public List<string> EmailsIds { get; set; }

            public EmailSequenceSendoutParameters Parameters { get; set; }
        }

        private class EmailMessage
        {
            public string Id { get; set; }

            public EmailProcessingStatus? Status { get; set; } = EmailProcessingStatus.Draft;
        }

        private class EmailSequenceSendoutParameters
        {
            public string RecipientAddress { get; set; }

            public string RecipientDisplay { get; set; }

            public Dictionary<string, string> Data { get; set; } = new Dictionary<string, string>();
        }

        private enum EmailProcessingStatus
        {
            Draft = 1,
            Queued = 2,
            Sent = 3,
            Cancelled = 4,
            Failed = 5
        }
    }
}
