﻿using System;
using System.Collections.Generic;
using System.Linq;
using Sparrow.Json.Parsing;

namespace Raven.Client.Documents.Operations.ETL.Queue
{
    public class QueueEtlConfiguration : EtlConfiguration<QueueConnectionString>
    {
        public QueueEtlConfiguration()
        {
            Queues = new List<EtlQueue>();
        }

        public List<EtlQueue> Queues { get; set; }

        public QueueBrokerType BrokerType { get; set; }

        public bool SkipAutomaticQueueDeclaration { get; set; }

        public override bool Validate(out List<string> errors, bool validateName = true, bool validateConnection = true)
        {
            var validationResult = base.Validate(out errors, validateName, validateConnection);
            if (Connection != null && BrokerType != Connection.BrokerType)
            {
                errors.Add("Broker type must be the same in the ETL configuration and in Connection string.");
                return false;
            }
            return validationResult;
        }
        
        public override string GetDestination()
        {
            return Connection.GetUrl();
        }

        public override EtlType EtlType => EtlType.Queue;
        
        public override bool UsingEncryptedCommunicationChannel()
        {
            switch (BrokerType)
            {
                case QueueBrokerType.Kafka:
                    if (Connection.KafkaConnectionSettings.ConnectionOptions.ContainsKey("SecurityProtocol"))
                    {
                        string protocol = Connection.KafkaConnectionSettings.ConnectionOptions["SecurityProtocol"];
                        return protocol.ToLower() == "ssl";
                    }
                    break;
                case QueueBrokerType.RabbitMq:
                    return Connection.RabbitMqConnectionSettings.ConnectionString.StartsWith("amqp", StringComparison.OrdinalIgnoreCase);
                default:
                    return false;
            }

            return false;
        }

        public override string GetDefaultTaskName()
        {
            return $"Queue ETL to {ConnectionStringName}";
        }
        
        public override DynamicJsonValue ToJson()
        {
            var json = base.ToJson();

            json[nameof(BrokerType)] = BrokerType;
            json[nameof(SkipAutomaticQueueDeclaration)] = SkipAutomaticQueueDeclaration;
            json[nameof(Queues)] = new DynamicJsonArray(Queues.Select(x => x.ToJson()));

            return json;
        }
    }
    
    public class EtlQueue
    {
        public string Name { get; set; }

        public bool DeleteProcessedDocuments { get; set; }

        public DynamicJsonValue ToJson()
        {
            return new DynamicJsonValue
            {
                [nameof(Name)] = Name,
                [nameof(DeleteProcessedDocuments)] = DeleteProcessedDocuments
            };
        }
    }
}
