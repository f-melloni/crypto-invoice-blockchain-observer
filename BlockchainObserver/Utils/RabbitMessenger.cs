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
using Microsoft.AspNetCore.Hosting;
using System.Threading;

namespace BlockchainObserver.Utils
{
    public static class RabbitMessenger
    {
        private static string QueueOut;
        private static string QueueIn;
        private static string HostName;
        private static string Exchange;
        private static string SentryUrl;
        private static ConnectionFactory factory;
        private static IModel channel;
        private static IConnection connection;
        private static IBasicProperties properties;
        private static EventingBasicConsumer Consumer;
        private static RavenClient ravenClient;

        public static void Setup(IConfiguration configuration, IHostingEnvironment env)
        {
            SentryUrl = configuration["SentryClientUrl"];
            ravenClient = new RavenClient(SentryUrl);
            
            try {
                string key = env.IsDevelopment() ? "Development" : "Production";

                string UserName = configuration[$"RabbitMQ:{key}:UserName"];
                string Password = configuration[$"RabbitMQ:{key}:Password"];
                QueueOut = configuration["RabbitMQ:QueueOut"];
                QueueIn = configuration["RabbitMQ:QueueIn"];
                HostName = configuration[$"RabbitMQ:{key}:HostName"];
                Exchange = configuration["RabbitMQ:Exchange"];
                factory = new ConnectionFactory {
                    UserName = UserName,
                    Password = Password,
                    HostName = HostName,
                };

                TryCreateConnection(null, null);
            }
            catch (Exception ex) {
                ravenClient.Capture(new SentryEvent(ex));
            }
        }

        private static void TryCreateConnection(object sender, ShutdownEventArgs e)
        {
            try {
                CreateConnection();
            }
            catch (Exception) {
                Thread.Sleep(2000);
                TryCreateConnection(null, null);
            }
        }

        private static void CreateConnection()
        {
            Connect(null, null);
            connection.ConnectionShutdown += TryCreateConnection;
            channel.ModelShutdown += CreateChannel;

            properties = channel.CreateBasicProperties();
            properties.Persistent = true;

            Consumer = new EventingBasicConsumer(channel);
            Consumer.Received += (ch, ea) => {
                JObject body = JObject.Parse(Encoding.UTF8.GetString(ea.Body));

                //Parse WatchAddress message
                Observer.ParseMessage(body);
                channel.BasicAck(ea.DeliveryTag, false);
            };
            String consumerTag = channel.BasicConsume(QueueIn, false, Consumer);
        }

        private static void CreateChannel(object sender, ShutdownEventArgs e)
        {
            try {
                channel = connection.CreateModel();
            }
            catch (Exception ex) {
                ravenClient.Capture(new SentryEvent(ex));
            }
        }

        private static void Connect(object sender, ShutdownEventArgs e)
        {
            try {
                connection = factory.CreateConnection();
                CreateChannel(null, null);
            }
            catch (Exception ex) {
                ravenClient.Capture(new SentryEvent(ex));
            }
        }

        public static void Send(string message)
        {
            Send(new string[] { message });
        }

        public static void Send(string[] messages)
        {
            try 
            {
                foreach (string message in messages) 
                {
                    if (connection == null || !connection.IsOpen) {
                        TryCreateConnection(null, null);
                    }

                    if (channel.IsClosed) {
                        channel = connection.CreateModel();
                    }

                    byte[] body = Encoding.UTF8.GetBytes(message);
                    channel.BasicPublish("", QueueOut, properties, body);
                }
            }
            catch (Exception ex) {
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
