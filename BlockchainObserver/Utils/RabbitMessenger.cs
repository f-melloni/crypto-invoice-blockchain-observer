using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RabbitMQ.Client;
using Microsoft.Extensions.Configuration;
using System.Text;
using RabbitMQ.Client.Events;
using Newtonsoft.Json.Linq;
using SharpRaven;
using SharpRaven.Data;

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
        private static RavenClient ravenClient = new RavenClient(@"http://150379555fca4cf3b1145013d8d740c7:e237b7c99d944bec8a053f81a31f97a3@185.59.209.146:38082/2");

        public static void Setup(IConfiguration configuration)
        {
            try
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

                Connect(null, null);
                connection.ConnectionShutdown += Connect;
                channel.ModelShutdown += CreateChannel;

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
            }catch(Exception ex)
            {
                 ravenClient = new RavenClient(@"http://150379555fca4cf3b1145013d8d740c7:e237b7c99d944bec8a053f81a31f97a3@185.59.209.146:38082/2");
                ravenClient.Capture(new SentryEvent(ex));

            }
        }

        private static void CreateChannel(object sender, ShutdownEventArgs e)
        {
            try
            {
                channel = connection.CreateModel();
            }
            catch(Exception ex)
            {
                 ravenClient = new RavenClient(@"http://150379555fca4cf3b1145013d8d740c7:e237b7c99d944bec8a053f81a31f97a3@185.59.209.146:38082/2");
                ravenClient.Capture(new SentryEvent(ex));

            }
            System.Threading.Thread.Sleep(2500);
        }

        private static void Connect(object sender, ShutdownEventArgs e)
        {
            try
            {
                connection = factory.CreateConnection();
                CreateChannel(null, null);
            }catch(Exception ex)
            {
                ravenClient.Capture(new SentryEvent(ex));

            }
            System.Threading.Thread.Sleep(2500);
        }

        public static void Send(string message)
        {
            Send(new string[]{ message });
        }

        public static void Send(string[] messages)
        {
            try
            {
                foreach (string message in messages)
                {
                    if (channel.IsClosed)
                        channel = connection.CreateModel();
                    byte[] body = Encoding.UTF8.GetBytes(message);
                    channel.BasicPublish("", QueueOut, properties, body);
                }
            }catch(Exception ex)
            {
                 ravenClient = new RavenClient(@"http://150379555fca4cf3b1145013d8d740c7:e237b7c99d944bec8a053f81a31f97a3@185.59.209.146:38082/2");
                ravenClient.Capture(new SentryEvent(ex));

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
