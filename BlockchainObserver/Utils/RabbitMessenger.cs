using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RabbitMQ.Client;
using Microsoft.Extensions.Configuration;
using System.Text;
using RabbitMQ.Client.Events;
using Newtonsoft.Json.Linq;

namespace BlockchainObserver.Utils
{
    public static class RabbitMessenger
    {
        private static string QueueOut;
        private static string QueueIn;
        private static string HostName;
        private static string Exchange;
        private static ConnectionFactory factory;
        private static IModel channel;
        private static IConnection connection;
        private static IBasicProperties properties;
        private static EventingBasicConsumer Consumer;

        public static void Setup(IConfiguration configuration)
        {
            string UserName = configuration["RabbitMQ:UserName"];
            string Password = configuration["RabbitMQ:Password"];
            QueueOut = configuration["RabbitMQ:QueueOut"];
            QueueIn = configuration["RabbitMQ:QueueIn"];
            HostName = configuration["RabbitMQ:HostName"];
            Exchange = configuration["RabbitMQ:Exchange"];
            factory = new ConnectionFactory
            {
                UserName = UserName,
                Password = Password,
                HostName = HostName,
            };

            connection = factory.CreateConnection();
            channel = connection.CreateModel();

            properties = channel.CreateBasicProperties();
            properties.Persistent = true;

            Consumer = new EventingBasicConsumer(channel);
            Consumer.Received += (ch, ea) =>
            {
                JObject body = JObject.Parse(Encoding.UTF8.GetString(ea.Body));
                //Parse WatchAddress message
                Observer.ParseMessage(body);
                channel.BasicAck(ea.DeliveryTag, false);
            };
            String consumerTag = channel.BasicConsume(QueueIn, false, Consumer);
        }


        public static void Send(string message)
        {
            Send(new string[]{ message });
        }

        public static void Send(string[] messages)
        {
            foreach (string message in messages)
            {
                if (channel.IsClosed)
                    channel = connection.CreateModel();
                byte[] body = Encoding.UTF8.GetBytes(message);
                channel.BasicPublish("", QueueOut, properties, body);
            }
        }

        public static void Close()
        {
            channel.Close();
            connection.Close(0);
            connection.Dispose();
        }
    }
}
