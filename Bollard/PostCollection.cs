using System;
using System.Collections.Generic;
using System.Drawing.Text;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BollardBlogger {
	public abstract class PostCollection {
		private List<dynamic> m_Pages = new List<dynamic>();

		protected PostCollection(Site site, string name, string layout) {
			Site = site;
			Name = name;
			Layout = layout;
		}

		public Site Site { get; private set; }
		public string Name { get; private set; }
		public List<dynamic> Pages { get { return m_Pages; } }
		public string Layout { get; private set; }
		public string Path => "/" + Name;

		public abstract void Prep();

		public abstract void Render();

	}
}
