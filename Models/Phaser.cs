using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tekla.Structures.Model;
using Tekla.Structures.Model.Operations;

namespace RazorCX.Phaser.Models
{
	public class Phaser
	{
		private readonly Model _model;
		private List<Part> _parts;
		private List<Part> _mainParts;
		private List<Part> _secondaryParts;
		private List<PartSummary> _mainPartSummaries;
		private Operation.ProgressBar _progressBar;
		private List<Results> _results;
        private List<string> _statusLog;

		public Phaser()
		{
			_model = new Model();
			_mainParts = new List<Part>();
			_secondaryParts = new List<Part>();
			_progressBar = new Operation.ProgressBar();
			_progressBar.Close();
			_results = new List<Results>();
            _statusLog = new List<string>();
        }

		public void Process()
		{
			try
			{

				Log("Getting Model Object Selection");
				GetSelection();
				Log("Processing Main and Secondary Parts");
				GetMainAndSecondaryParts();
				Log("Processing Assemblies and Secondary Parts, Connections, Details and Bolt Groups");
				GetMainPartSummaries();
				Log("Checking and Fixing Phases");
				FixAllPartPhases();

                var modelInfo = _model.GetInfo();
                File.WriteAllLines($@".\{modelInfo.ModelName.Split('.').First()}_PhaserLog_{DateTime.UtcNow.ToFileTimeUtc()}.rcx", _statusLog);

                //_results.ForEach(r =>
                //{
                //	Log(r.ModelObject.GetType().Name + " | " + r.PhaseBefore.PhaseNumber + " | " +
                //		                  r.PhaseAfter.PhaseNumber, "Results");
                //});
            }
			catch (Exception ex)
			{
				_progressBar.Close();
			}
			finally
			{
				_progressBar.Close();
			}
		}

		private void GetSelection()
		{
			_parts = _model.GetSelectedObjects<Part>();

			//sort for viewing
			_parts = _parts.OrderBy(p => p.GetPhase().PhaseNumber)
				.ThenBy(p => p.Profile.ProfileString).ToList();

		}

		private void GetMainAndSecondaryParts()
		{
			_parts.ForEach(p =>
			{
				if (p.IsMainPart())
					_mainParts.Add(p);
				else _secondaryParts.Add(p);
			});
		}

		private void GetMainPartSummaries()
		{
			//_progressBar = new Operation.ProgressBar();
			var displayResult = _progressBar.Display(100, "Progress", "Creating Part Summaries", "Cancel", " ");

            try
			{
				var index = 0;
				_mainPartSummaries = _mainParts.Select(p =>
				{
					var assembly = p.GetAssembly();
					var secondaries = assembly.GetSecondaries().OfType<Part>().ToList();
					var boltGroups = p.GetBolts().ToAList<BoltGroup>();
					var welds = p.GetWelds().ToAList<BaseWeld>();

					var partSummary = new PartSummary
					{
						MainPart = p,
						Secondaries = secondaries,
						BoltGroups = boltGroups,
						Welds = welds
					};

					index++;
					var message = $"{p.Profile.ProfileString} ({index}/{_mainParts.Count})";
					//if (displayResult)
					//	_progressBar.SetProgress(message, 100 * index / _mainParts.Count);

					Log(message, $"Phase {p.GetPhase().PhaseNumber} | {p.GetPartMark()}");

					return partSummary;
				}).ToList();
			}
			catch (Exception ex)
			{
				_progressBar.Close();
			}
			finally
			{
				_progressBar.Close();
			}
		}

		public class Results
		{
			public ModelObject ModelObject { get; set; }
			public Phase PhaseBefore { get; set; }
			public Phase PhaseAfter { get; set; }
		}

		private void FixAllPartPhases()
		{
			try
			{
				_progressBar = new Operation.ProgressBar();
				//var displayResult =
				//	_progressBar.Display(100, "Progress", "Checking/Fixing Detail Phases", "Cancel", " ");

				var index = 0;

				_mainPartSummaries?.ForEach(p =>
				{
					FixSecondaryComponentPhases(p.Secondaries);
					FixSecondaryPartPhases(p.Secondaries);
					FixBoltGroupPhases(p.BoltGroups);
					FixSecondaryBoltGroupPhases(p.Secondaries);
					FixWeldPhases(p.Welds);

					index++;
					var message = $"{p.MainPart.Profile.ProfileString} ({index}/{_mainPartSummaries.Count})";
					//if (displayResult)
					//	_progressBar.SetProgress(message, 100 * index / _mainPartSummaries.Count);

					Log(message, $"Phase {p.MainPart.GetPhase().PhaseNumber} | {p.MainPart.GetPartMark()}");

				});
			}
			catch (Exception ex)
			{
				_progressBar.Close();
			}
			finally
			{
				_progressBar.Close();
				_model.CommitChanges();
			}
		}

		private void FixSecondaryComponentPhases(List<Part> parts)
		{
			parts.ForEach(FixConnectionAndDetailPhase);
		}

		private void FixConnectionAndDetailPhase(Part part)
		{
			var baseComponent = part.GetFatherComponent();
			switch (baseComponent)
			{
				case null:
					return;
				case Connection connection:
				{
					FixConnectionPhase(connection);
					return;
				}
				case Detail detail:
				{
					FixDetailPhase(detail);
					return;
				}
			}
		}

		private void FixConnectionPhase(Connection connection)
		{
			var pPhase = connection.GetPrimaryObject().GetPhase();
			var cPhase = connection.GetPhase();

			if (pPhase?.PhaseNumber == cPhase?.PhaseNumber) return;

			if (connection.SetPhase(pPhase))
				_results.Add(new Results
				{
					ModelObject = connection,
					PhaseBefore = cPhase,
					PhaseAfter = pPhase
				});
			else
			{
				_results.Add(new Results
				{
					ModelObject = connection,
					PhaseBefore = cPhase,
					PhaseAfter = cPhase
				});
			}

			var result = _results.Last();
			var message = $"Phase No: {result.PhaseBefore.PhaseNumber} Changed to Phase No: {result.PhaseAfter.PhaseNumber}";
			var resultStatus = result.PhaseBefore != result.PhaseAfter ? "Fixed" : "OK";

			Log(message, $"____{connection.Name} ({connection.Number})", resultStatus);

		}

		private void FixDetailPhase(Detail detail)
		{
			var pPhase = detail.GetPrimaryObject().GetPhase();
			var dPhase = detail.GetPhase();

			if (pPhase?.PhaseNumber == dPhase?.PhaseNumber) return;

			if (detail.SetPhase(pPhase))
				_results.Add(new Results
				{
					ModelObject = detail,
					PhaseBefore = dPhase,
					PhaseAfter = pPhase
				});
			else
			{
				_results.Add(new Results
				{
					ModelObject = detail,
					PhaseBefore = dPhase,
					PhaseAfter = dPhase
				});
			}

			var result = _results.Last();
			var message = $"Phase No: {result.PhaseBefore.PhaseNumber} Changed to Phase No: {result.PhaseAfter.PhaseNumber}";
			var resultStatus = result.PhaseBefore != result.PhaseAfter ? "Fixed" : "OK";

			Log(message, $"____{detail.Name} ({detail.Number})", resultStatus);

		}

		private void FixSecondaryPartPhases(List<Part> parts)
		{
			parts.ForEach(part =>
			{
				var primary = part.GetAssembly().GetMainPart() as Part;
				var primaryPhase = primary.GetPhase();
				var secondaryPhase = part.GetPhase();

				if (primaryPhase?.PhaseNumber == secondaryPhase?.PhaseNumber) return;

				if (part.SetPhase(primaryPhase))
					_results.Add(new Results
					{
						ModelObject = part,
						PhaseBefore = secondaryPhase,
						PhaseAfter = primaryPhase
					});
				else
				{
					_results.Add(new Results
					{
						ModelObject = part,
						PhaseBefore = secondaryPhase,
						PhaseAfter = secondaryPhase
					});
				}

				var result = _results.Last();
				var message = $"Phase No: {result.PhaseBefore.PhaseNumber} Changed to Phase No: {result.PhaseAfter.PhaseNumber}";
				var resultStatus = result.PhaseBefore != result.PhaseAfter ? "Fixed" : "OK";

				Log(message, $"____{part.Name} | {part.Profile.ProfileString}", resultStatus);

			});
		}

		private void FixBoltGroupPhases(List<BoltGroup> boltGroups)
		{
			boltGroups.ForEach(boltGroup =>
			{
				var primary = boltGroup.PartToBoltTo;

				var primaryPhase = primary.GetPhase();
				var boltGroupPhase = boltGroup.GetPhase();

				if (primaryPhase?.PhaseNumber == boltGroupPhase?.PhaseNumber) return;

				if (boltGroup.SetPhase(primaryPhase))
					_results.Add(new Results
					{
						ModelObject = boltGroup,
						PhaseBefore = boltGroupPhase,
						PhaseAfter = primaryPhase
					});
				else
				{
					_results.Add(new Results
					{
						ModelObject = boltGroup,
						PhaseBefore = boltGroupPhase,
						PhaseAfter = boltGroupPhase
					});
				}

				var result = _results.Last();
				var message = $"Phase No: {result.PhaseBefore.PhaseNumber} Changed to Phase No: {result.PhaseAfter.PhaseNumber}";
				var resultStatus = result.PhaseBefore != result.PhaseAfter ? "Fixed" : "OK";

				Log(message, $"____{Math.Round(boltGroup.BoltSize, 2)} | {boltGroup.BoltStandard}", resultStatus);

			});
		}

		private void FixSecondaryBoltGroupPhases(List<Part> parts)
		{
			var secondaryBolts = new HashSet<BoltGroup>();
			parts.ForEach(secondary =>
			{
				var secBolts = secondary.GetBolts().ToAList<BoltGroup>();
				secBolts.ForEach(b => secondaryBolts.Add(b));
			});

			FixBoltGroupPhases(secondaryBolts.ToList());
		}

		private void FixWeldPhases(List<BaseWeld> welds)
		{
			welds.ForEach(weld =>
			{
				var primary = weld.MainObject;

				var primaryPhase = primary.GetPhase();
				var secondaryPhase = weld.GetPhase();

				if (primaryPhase?.PhaseNumber == secondaryPhase?.PhaseNumber) return;

				if (weld.SetPhase(primaryPhase))
					_results.Add(new Results
					{
						ModelObject = weld,
						PhaseBefore = secondaryPhase,
						PhaseAfter = primaryPhase
					});
				else
				{
					_results.Add(new Results
					{
						ModelObject = weld,
						PhaseBefore = secondaryPhase,
						PhaseAfter = secondaryPhase
					});
				}

				var result = _results.Last();
				var message = $"Phase No: {result.PhaseBefore.PhaseNumber} Changed to Phase No: {result.PhaseAfter.PhaseNumber}";
				var resultStatus = result.PhaseBefore != result.PhaseAfter ? "Fixed" : "OK";

				Log(message, $"____{weld.TypeBelow}: {Math.Round(weld.SizeBelow, 2)} | {weld.TypeAbove} {weld.SizeAbove}", resultStatus);

			});
		}

		private void Log(string message, string method = "Phaser Status", string result = "")
		{
			var log = string.Format("{0,-60} {1,-40} {2, -10}", $"[{method}]", 
				//$"[{DateTime.Now.ToShortDateString()} {DateTime.Now.ToLongTimeString()}]", 
				message,
				result);

			//var log = $"[{method}] {DateTime.Now.ToShortDateString()} {DateTime.Now.ToLongTimeString()}:  {message}";
			Console.WriteLine(log);
            _statusLog.Add(log);
        }
	}
}
