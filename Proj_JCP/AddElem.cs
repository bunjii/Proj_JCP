using System;
using System.Collections.Generic;
using System.Linq;

using Grasshopper.Kernel;
using Rhino.Geometry;

using Karamba.Models;
using Karamba.Elements;
using feb;
using Karamba.Nodes;
using System.Collections.ObjectModel;
using Karamba.Geometry;
using Grasshopper.Kernel.Geometry;
using Karamba.GHopper.Results;
using Rhino.Display;
using Proj_JCP.Properties;

namespace Proj_JCP
{
    public class AddElem : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the AddElem class.
        /// </summary>
        public AddElem()
          : base("AddElem", "Nickname",
              "Description",
              "DDL", "JCP")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("91c26b29-9cc7-48a3-b0e0-178c0d2693ab"); }
        }
    }
}