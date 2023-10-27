using System.Collections.Generic;
using Tekla.Structures.Model;

namespace RazorCX.Phaser.Models
{
	public class PartSummary
	{
		public Part MainPart { get; set; }
		public List<Part> Secondaries { get; set; }
		public List<Connection> Connections { get; set; }
		public List<Detail> Details { get; set; }
		public List<BoltGroup> BoltGroups { get; set; }
		public List<BaseWeld> Welds { get; set; }
	}
}
