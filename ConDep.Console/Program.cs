﻿using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
using ConDep.Dsl;
using ConDep.Dsl.Config;
using ConDep.Dsl.Logging;
using ConDep.Dsl.Operations;
using ConDep.Dsl.SemanticModel;
using ConDep.Dsl.SemanticModel.WebDeploy;
using ConDep.WebQ.Client;
using ConDep.WebQ.Data;

namespace ConDep.Console
{
    sealed internal class Program
    {
        static void Main(string[] args)
        {
            var exitCode = 0;
            WebQueue webQ = null;
            try
            {
                new LogConfigLoader().Load();

                var optionHandler = new CommandLineOptionHandler(args);

                if (optionHandler.Params.InstallWebQ)
                {
                    throw new NotImplementedException();
                }
                else
                {
                    Logger.LogSectionStart("ConDep");
                    if (!string.IsNullOrWhiteSpace(optionHandler.Params.WebQAddress))
                    {
                        webQ = new WebQueue(optionHandler.Params.WebQAddress, optionHandler.Params.Environment);
                        webQ.WebQueueInfo += (sender, eventArgs) => Logger.Info(eventArgs.Message);
                        Logger.LogSectionStart("Waiting in Deployment Queue");
                        try
                        {
                            webQ.WaitInQueue(TimeSpan.FromMinutes(30));
                        }
                        finally
                        {
                            Logger.LogSectionEnd("Waiting in Deployment Queue");
                        }
                    }

                    var configAssemblyLoader = new ConDepAssemblyHandler(optionHandler.Params.AssemblyName);
                    var assembly = configAssemblyLoader.GetAssembly();

                    var conDepOptions = new ConDepOptions(optionHandler.Params.DeployAllApps,
                                                          optionHandler.Params.Application,
                                                          optionHandler.Params.DeployOnly,
                                                          optionHandler.Params.WebDeployExist,
                                                          optionHandler.Params.StopAfterMarkedServer,
                                                          optionHandler.Params.ContinueAfterMarkedServer,
                                                          assembly);
                    var envSettings = GetEnvConfig(optionHandler.Params, assembly);

                    var status = new WebDeploymentStatus();
                    ConDepConfigurationExecutor.ExecuteFromAssembly(assembly, envSettings, conDepOptions, status);

                    if (status.HasErrors)
                    {
                        exitCode = 1;
                    }
                    else
                    {
                        status.EndTime = DateTime.Now;
                        status.PrintSummary();
                    }
                }
            }
            catch (Exception ex)
            {
                exitCode = 1;
                Logger.Error("ConDep reported a fatal error:");
                Logger.Error("Message: " + ex.Message);
                Logger.Error("Stack trace:\n" + ex.StackTrace);
            }
            finally
            {
                if(webQ != null)
                {
                    webQ.LeaveQueue();
                }

                Logger.LogSectionEnd("ConDep");
                Environment.Exit(exitCode);
            }
        }

        private static ConDepConfig GetEnvConfig(CommandLineParams cmdParams, Assembly assembly)
        {
            var envFileName = string.Format("{0}.Env.json", cmdParams.Environment);
            var envFilePath = Path.Combine(Path.GetDirectoryName(assembly.Location), envFileName);

            var jsonConfigParser = new EnvConfigParser();
            var envConfig = jsonConfigParser.GetEnvConfig(envFilePath);
            envConfig.EnvironmentName = cmdParams.Environment;

            if (cmdParams.BypassLB)
            {
                envConfig.LoadBalancer = null;
            }
            return envConfig;
        }
    }
}
