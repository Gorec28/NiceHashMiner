﻿using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using MinerPlugin;
using MinerPlugin.Toolkit;
using NiceHashMinerLegacy.Common.Enums;
using static NiceHashMinerLegacy.Common.StratumServiceHelpers;
using static MinerPlugin.Toolkit.MinersApiPortsManager;
using System.Globalization;
using Newtonsoft.Json;
using System.Net;
using System.Net.Http;

namespace T_RexBase
{
    public class T_RexBase : MinerBase
    {
        private AlgorithmType _algorithmType;

        private string _devices;
        private string _extraLaunchParameters = "";
        private int _apiPort;
        private HttpClient _httpClient;


        private ApiDataHelper apiReader = new ApiDataHelper();

        protected virtual string AlgorithmName(AlgorithmType algorithmType)
        {
            switch (algorithmType)
            {
                case AlgorithmType.Lyra2Z: return "lyra2z";
                case AlgorithmType.Skunk: return "skunk";
                case AlgorithmType.X16R: return "x16r";
            }
            return "";
        }

        public async override Task<ApiData> GetMinerStatsDataAsync()
        {
            if (_httpClient == null) _httpClient = new HttpClient();
            var ad = new ApiData();
            try
            {
                var summaryApiResult = await _httpClient.GetStringAsync($"http://127.0.0.1:{_apiPort}/summary");
                var summary = JsonConvert.DeserializeObject<JsonApiResponse>(summaryApiResult);
            
                var gpuDevices = _miningPairs.Select(pair => pair.device);
                var perDeviceSpeedInfo = new List<(string uuid, IReadOnlyList<(AlgorithmType, double)>)>();
                var perDevicePowerInfo = new List<(string, int)>();
                var totalSpeed = summary.hashrate;
                var totalPowerUsage = 0.0;

                foreach (var gpuDevice in gpuDevices)
                {
                    var currentStats = summary.gpus.Where(devStats => devStats.gpu_id == gpuDevice.ID).FirstOrDefault();
                    if (currentStats == null) continue;
                    perDeviceSpeedInfo.Add((gpuDevice.UUID, new List<(AlgorithmType, double)>() { (_algorithmType, currentStats.hashrate) }));
                    totalPowerUsage += currentStats.power;
                    perDevicePowerInfo.Add((gpuDevice.UUID, currentStats.hashrate));
                }

                var total = new List<(AlgorithmType, double)>();
                total.Add((_algorithmType, totalSpeed));
                ad.AlgorithmSpeedsTotal = total;
                ad.PowerUsageTotal = Convert.ToInt32(totalPowerUsage);
                ad.AlgorithmSpeedsPerDevice = perDeviceSpeedInfo;
                ad.PowerUsagePerDevice = perDevicePowerInfo;

            }
            catch (Exception ex) {
                Console.WriteLine(ex.Message);
             }

            return ad;
        }

        public override async Task<(double speed, bool ok, string msg)> StartBenchmark(CancellationToken stop, BenchmarkPerformanceType benchmarkType = BenchmarkPerformanceType.Standard)
        {
            var avgRet = 0.0;
            var counter = 0;
            var after = "Total:";

            // determine benchmark time 
            // settup times
            var benchmarkTime = 20; // in seconds
            switch (benchmarkType)
            {
                case BenchmarkPerformanceType.Quick:
                    benchmarkTime = 20;
                    break;
                case BenchmarkPerformanceType.Standard:
                    benchmarkTime = 60;
                    break;
                case BenchmarkPerformanceType.Precise:
                    benchmarkTime = 120;
                    break;
            }

            var algo = AlgorithmName(_algorithmType);

            var commandLine = $"--algo {algo} --benchmark --time-limit {benchmarkTime}";
            var (binPath, binCwd) = GetBinAndCwdPaths();
            var bp = new BenchmarkProcess(binPath, binCwd, commandLine);

            bp.CheckData = (string data) =>
            {
                var s = data;
                Console.WriteLine(s);
                var ret = (hashrate: default(double), found: false);
                if (s.Contains("WARN: Time limit is reached"))
                {
                    ret.hashrate = avgRet / counter;
                    ret.found = true;
                }
                else
                {
                    if (!s.Contains(after))
                    {
                        return ret;
                    }
                    var afterString = s.GetStringAfter(after).ToLower();
                    var numString = new string(afterString
                        .ToCharArray()
                        .SkipWhile(c => !char.IsDigit(c))
                        .TakeWhile(c => char.IsDigit(c) || c == '.')
                        .ToArray());

                    if (!double.TryParse(numString, NumberStyles.Float, CultureInfo.InvariantCulture, out var hash))
                    {
                        return ret;
                    }

                    counter++;
                    if (afterString.Contains("kh"))
                        avgRet += hash * 1000;
                    else if (afterString.Contains("mh"))
                        avgRet += hash * 1000000;
                    else if (afterString.Contains("gh"))
                        avgRet += hash * 1000000000;
                    else
                        avgRet += hash;

                }
                return ret;
            };
            
            var benchmarkTimeout = TimeSpan.FromSeconds(benchmarkTime + 5);
            var benchmarkWait = TimeSpan.FromMilliseconds(500);
            var t = MinerToolkit.WaitBenchmarkResult(bp, benchmarkTimeout, benchmarkWait, stop);
            return await t;
        }


        protected override (string, string) GetBinAndCwdPaths()
        {
            var binPath = @"C:\Users\Domen\Desktop\nhml\bin_3rdparty\trex\t-rex.exe";
            var binCwd = @"C:\Users\Domen\Desktop\nhml\bin_3rdparty\trex";
            return (binPath, binCwd);
        }

        protected override void Init()
        {
            bool ok;
            (_algorithmType, ok) = MinerToolkit.GetAlgorithmSingleType(_miningPairs);
            if (!ok) throw new InvalidOperationException("Invalid mining initialization");
            // all good continue on

            // init command line params parts
            var deviceIds = MinerToolkit.GetDevicesIDsInOrder(_miningPairs);
            _devices = $"--devices {string.Join(",", deviceIds)}";
            // TODO implement this later
            //_extraLaunchParameters;
        }

        protected override string MiningCreateCommandLine()
        {
            // API port function might be blocking
            _apiPort = GetAvaliablePortInRange(); // use the default range
            // instant non blocking
            var url = GetLocationUrl(_algorithmType, _miningLocation, NhmConectionType.STRATUM_TCP);
            var algo = AlgorithmName(_algorithmType);

            var commandLine = $"--algo {algo} --url {url} --user {_username} --api-bind-http 127.0.0.1:{_apiPort} {_extraLaunchParameters}";
            return commandLine;
        }
    }
}

