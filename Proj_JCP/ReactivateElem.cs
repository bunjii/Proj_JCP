using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Proj_JCP
{
    public class ReactivateElem : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the ReactivateElem class.
        /// </summary>
        public ReactivateElem()
          : base("ReactivateElem", "ReactivateElem",
              "ReactivateElem",
              "DDL", "JCP")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddParameter(new Karamba.GHopper.Models.Param_Model(), "inModel", "inModel", "Model to be manipulated", GH_ParamAccess.item);
            pManager.AddIntegerParameter("ids", "ids", "", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddParameter(new Karamba.GHopper.Models.Param_Model(), "outModel", "outModel", "output of Karamba Model after manipulation", GH_ParamAccess.item);
            pManager.AddLineParameter("reactivatedLns", "rLns", "", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- variables ---
            Karamba.GHopper.Models.GH_Model in_gh_model = null;

            List<int> eids = new List<int>();
            List<Line> rLns = new List<Line>();

            // --- input ---
            if (!DA.GetData(0, ref in_gh_model)) return;
            Karamba.Models.Model model = in_gh_model.Value;

            DA.GetDataList(1, eids);

            // --- solve ---
            // clone model to avoid side effects
            model = model.Clone();

            // clone its elements to avoid side effects
            model.cloneElements();

            // clone the feb-model to avoid side effects
            model.deepCloneFEModel();

            string singular_system_msg = "The stiffness matrix of the system is singular.";

            foreach (int i in eids)
            {
                model.elems[i].set_is_active(model, true);

                var sp = model.nodes[model.elems[i].node_inds[0]].pos;
                var ep = model.nodes[model.elems[i].node_inds[1]].pos;
                rLns.Add(new Line(sp.X, sp.Y, sp.Z, ep.X, ep.Y, ep.Z));


            }

            // if something changed inform the feb-model about it
            // (otherwise it won't recalculate)
            model.febmodel.touch();

            // update the model to its final state
            try
            {
                // create a deform and response object for calculating and retrieving results
                feb.Deform deform = new feb.Deform(model.febmodel);
                feb.Response response = new feb.Response(deform);

                // calculate the displacement
                response.updateNodalDisplacements();

                // calculate the member forces
                response.updateMemberForces();


                // this guards the objects from being freed prematurely
                deform.Dispose();
                response.Dispose();
            }
            catch
            {
                // send an error message in case something went wrong
                throw new Exception(singular_system_msg);
            }


            // --- output ---
            DA.SetData(0, new Karamba.GHopper.Models.GH_Model(model));
            DA.SetDataList(1, rLns);



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
            get { return new Guid("03cbae99-a219-4142-a10c-70aea5a8644d"); }
        }
    }
}