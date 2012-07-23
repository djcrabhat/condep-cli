﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ConDep.Dsl.Core;
using NDesk.Options;
using Newtonsoft.Json.Linq;

namespace ConDep.Console
{
    class Program
    {
        //Blue/Green deployment - with possibillity to roll back to blue/green

        //ConDep.exe MyAssembly.dll Env=Test [Server=web01] [Applications=Selvbetjent] [/InfraOnly] [/DeployOnly]
        //
        //If only assembly and Env is provided, then ConDep will deploy all applications to all servers utilizing load balancer if provided
        //If assembly, Env and Server is provided, then ConDep will take Server offline from Load Balancer if provided and deploy all applications
        //If assembly, Env, Server and Application is provided, then ConDep will take Server offline from Load Balancer if provided and deploy only the Applications specified
        static void Main(string[] args)
        {
            string environment = "";
            string server = "";
            string application = "";
            bool infraOnly = false;
            bool deployOnly = false;
            bool showHelp = false;

            var optionSet = new OptionSet()
                                {
                                    {"s|server", "Server to deploy to", v => server = v},
                                    {"a|application", "Application to deploy", v => application = v},
                                    {"i|infraOnly", "Deploy infrastructure only", v => infraOnly = v != null },
                                    {"d|deployOnly", "Deploy all except infrastructure", v => deployOnly = v != null},
                                    {"h|help",  "show this message and exit", v => showHelp = v != null }
                                };
            try
            {
                optionSet.Parse(args);
            }
            catch(OptionException oe)
            {
                throw;
            }

            if (args.Length < 2)
            {
                PrintHelp(optionSet);
                return;
            }

            environment = args[1].Split('=')[1];

            if(showHelp)
            {
                PrintHelp(optionSet);
                return;
            }

            if(environment == null)
            {
                PrintHelp(optionSet);
                return;
            }

            var assembly = FindAssembly(args);
            var type = assembly.GetTypes().Where(t => typeof(ConDepConfigurator).IsAssignableFrom(t)).FirstOrDefault();

            var envJsonText = File.ReadAllText(Path.Combine(Path.GetDirectoryName(type.Assembly.Location), string.Format("{0}.Env.js", environment)));
            var webSiteJsonText = File.ReadAllText(Path.Combine(Path.GetDirectoryName(type.Assembly.Location), string.Format("WebSites.{0}.Env.js", environment)));

            var envJson = JObject.Parse(envJsonText);
            var webSiteJson = JObject.Parse(webSiteJsonText);

            var envSettings = PopulateEnvSettings(envJson);
            PopulateWebSiteSettings(envSettings, webSiteJson);

            Executor.ExecuteFromAssembly(assembly, envSettings);
        }

        private static void PopulateWebSiteSettings(ConDepEnvironmentSettings envSettings, JObject json)
        {
            foreach(JProperty webSite in json["WebSites"].Children())
            {
                var webSiteName = webSite.Name;

                foreach(JProperty server in webSite.Value)
                {
                    var envServer = envSettings.Servers.Where(x => x.ServerName == server.Name).FirstOrDefault();
                    var envWebSite = new ConDepWebSiteSettings(webSiteName);

                    foreach(var binding in server.Value)
                    {
                        var bindingType = binding["BindingType"].ToString();
                        var port = binding["Port"].ToString();
                        var ip = binding["Ip"].ToString();
                        var hostHeader = binding["HostHeader"].ToString();

                        envWebSite.Bindings.Add(new ConDepWebSiteBinding(bindingType, port, ip, hostHeader));
                    }

                    envServer.WebSites.Add(envWebSite);
                }
            }
        }

        private static ConDepEnvironmentSettings PopulateEnvSettings(JObject json)
        {
            var envSettings = new ConDepEnvironmentSettings();
            
            PopulateLoadBalancer(envSettings, json);
            foreach(var server in json["Servers"])
            {
                envSettings.Servers.Add(new DeploymentServer(server["Name"].ToString()));
            }
            if(envSettings.DeploymentUser.IsDefined)
            {
                envSettings.DeploymentUser.UserName = json["DeploymentUser"]["UserName"].ToString();
                envSettings.DeploymentUser.Password = json["DeploymentUser"]["Password"].ToString();
            }
            return envSettings;
        }

        private static void PopulateLoadBalancer(ConDepEnvironmentSettings envSettings, JObject json)
        {
            if(json["LoadBalancer"] != null)
            {
                envSettings.LoadBalancer.Name = json["LoadBalancer"]["Name"].ToString();
                envSettings.LoadBalancer.Provider = json["LoadBalancer"]["Provider"].ToString();
            }
        }

        private static Assembly FindAssembly(string[] args)
        {
            var assemblyName = args[0];
            //var currentPath = AppDomain.CurrentDomain.BaseDirectory;


            var assemblyFileName = assemblyName;//Path.Combine(currentPath, assemblyName);
            return Assembly.LoadFile(assemblyFileName);
        }

        private static void PrintHelp(OptionSet optionSet)
        {
            System.Console.WriteLine();
            System.Console.WriteLine("Deploy files and infrastructure to remote servers and environments");
            System.Console.WriteLine();
            System.Console.WriteLine("Usage: ConDep <assembly> <environment> [-options]");
            System.Console.WriteLine();
            System.Console.WriteLine("  <assembly>\t\tAssembly containing deployment setup");
            System.Console.WriteLine("  <environment>\t\tEnvironment to deploy to (e.g. Dev, Test etc)");
            System.Console.WriteLine();
            System.Console.WriteLine("where options include:");
            optionSet.WriteOptionDescriptions(System.Console.Out);
            System.Console.WriteLine();

            System.Console.WriteLine("Examples:");
            System.Console.WriteLine("\t(1) ConDep.exe MyAssembly.dll Dev");
            System.Console.WriteLine();
            System.Console.WriteLine("\t(2) ConDep.exe MyAssembly.dll Dev -s MyWebServer");
            System.Console.WriteLine();
            System.Console.WriteLine("\t(3) ConDep.exe MyAssembly.dll Dev -s MyWebServer -a MyWebApp");
            System.Console.WriteLine();
            System.Console.WriteLine("\t(4) ConDep.exe MyAssembly.dll Dev -a MyWebApp");
            System.Console.WriteLine();
            System.Console.WriteLine("\t(5) ConDep.exe MyAssembly.dll Dev -a MyWebApp -d");
            System.Console.WriteLine();
            System.Console.WriteLine("\t(6) ConDep.exe MyAssembly.dll Dev -a MyWebApp -i");
            System.Console.WriteLine();
            System.Console.WriteLine("\t(7) ConDep.exe MyAssembly.dll Dev -i");
            System.Console.WriteLine();
            System.Console.WriteLine("\t(8) ConDep.exe MyAssembly.dll Dev -d");
            System.Console.WriteLine();

            System.Console.WriteLine("Explanations:");
            System.Console.WriteLine("\t1 - Deploy setup found in MyAssembly.dll to all servers in");
            System.Console.WriteLine("\t    the Dev environment.");
            System.Console.WriteLine();
            System.Console.WriteLine("\t2 - Deploy setup found in MyAssembly.dll do the Dev environment, ");
            System.Console.WriteLine("\t    but only to the server MyWebServer.");
            System.Console.WriteLine();
            System.Console.WriteLine("\t3 - Same as above, except only deploys the application MyWebApp.");
            System.Console.WriteLine("\t    This also meens no infrastructure is deployed (-do option).");
            System.Console.WriteLine();
            System.Console.WriteLine("\t4 - Deploy the application MyWebApp to all servers in the");
            System.Console.WriteLine("\t    Dev environment. (here the -do option is implisit).");
            System.Console.WriteLine();
            System.Console.WriteLine("\t5 - Same as above, only here -do is explicitly set.");
            System.Console.WriteLine();
            System.Console.WriteLine("\t6 - This will result in an error, cause you cannot deploy");
            System.Console.WriteLine("\t    an application with the infrastructure only option set.");
            System.Console.WriteLine();
            System.Console.WriteLine("\t7 - Deploy infrastructure setup only.");
            System.Console.WriteLine();
            System.Console.WriteLine("\t8 - Will only deploy deployment specific setup and");
            System.Console.WriteLine("\t    not infrastrucutre.");
        }
    }
}
