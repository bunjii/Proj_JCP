using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;

using Karamba.Geometry;
using Karamba.CrossSections;
using Karamba.Elements;
using Karamba.GHopper;
using Karamba.Loads;
using Karamba.Materials;
using Karamba.Models;
using Karamba.Nodes;
using Karamba.Supports;
using Karamba.Results;
using Karamba.Utilities;
using Proj_JCP.Properties;

namespace Proj_JCP
{
    public class UpdateBeams : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the UpdateBeams class.
        /// </summary>
        public UpdateBeams()
          : base("UpdateBeams", "Nickname",
              "Description",
              "DDL", "JCP")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddParameter(new Karamba.GHopper.Models.Param_Model(), "inModel", "inModel", "Model to be manipulated", GH_ParamAccess.item);

            pManager.AddTextParameter("col id", "col id", "elemid subject to elimination", GH_ParamAccess.item);
            pManager.AddTextParameter("gird id", "gird id", "girder", GH_ParamAccess.item);
            pManager.AddTextParameter("beam id", "beam id", "beam subject to elimination", GH_ParamAccess.item);

        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddParameter(new Karamba.GHopper.Models.Param_Model(), "outModel", "outModel", "output of Karamba Model after manipulation", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- variables ---
            Karamba.GHopper.Models.GH_Model in_gh_model = null;
            string cid = "";
            string bid = "";
            string gid = "";

            Dictionary<int, List<int>> nodeBeamDic = new Dictionary<int, List<int>>();
            Dictionary<int, List<int>> nodeGirDic = new Dictionary<int, List<int>>();

            List<ModelElement> targetElems = new List<ModelElement>();

            // --- input ---
            if (!DA.GetData(0, ref in_gh_model)) return;
            Model model = in_gh_model.Value;
            if (!DA.GetData(1, ref cid)) return;
            DA.GetData(2, ref gid);
            DA.GetData(3, ref bid);

            // --- solve ---

            // clone model to avoid side effects
            model = model.Clone();

            // clone its elements to avoid side effects
            model.cloneElements();

            // clone the feb-model to avoid side effects
            model.deepCloneFEModel();

            // prepare error message
            string singular_system_msg = "The stiffness matrix of the system is singular.";

            // prepare nodeBeamDic
            // prepare nodeGirDic
            foreach (ModelElement e in model.elems)
            {
                if (e.id == bid) // bid = "B1"
                {
                    List<int> nids = new List<int>() { e.node_inds[0], e.node_inds[1] };

                    foreach (int n in nids)
                    {
                        if (nodeBeamDic.ContainsKey(n))
                        {
                            nodeBeamDic[n].Add(e.ind);
                        }
                        else
                        {
                            nodeBeamDic.Add(n, new List<int>() { e.ind });
                        }
                    }
                }

                else if (e.id == gid) // gid = "G1"
                {
                    List<int> nids = new List<int>() { e.node_inds[0], e.node_inds[1] };

                    foreach (int n in nids)
                    {
                        if (nodeGirDic.ContainsKey(n))
                        {
                            nodeGirDic[n].Add(e.ind);
                        }
                        else
                        {
                            nodeGirDic.Add(n, new List<int>() { e.ind });
                        }
                    }
                }

                else if (e.id.Contains(cid)) // cid = "C1"
                {
                    targetElems.Add(e);
                }
            }

            feb.Deform deform = new feb.Deform(model.febmodel);
            feb.Response response = new feb.Response(deform);

            try
            {
                // calculate the displacements
                response.updateNodalDisplacements();
                // calculate the member forces
                response.updateMemberForces();
            }
            catch
            {
                // send an error message in case something went wrong
                throw new Exception(singular_system_msg);
            }

            foreach (ModelElement elem in targetElems)
            {
                if (elem.is_active == false)
                {
                    continue;
                }

                if (nodeBeamDic.Count != 0)
                {
                    ElimElem.ReviveBeamActivity(nodeGirDic, nodeBeamDic, elem, model);
                }
            }

            // if something changed inform the feb-model about it
            // (otherwise it won't recalculate)
            model.febmodel.touch();

            // this guards the objects from being freed prematurely
            GC.KeepAlive(deform);
            GC.KeepAlive(response);

            // update the model to its final state
            try
            {
                // create a deform and response object for calculating and retrieving results
                deform = new feb.Deform(model.febmodel);
                response = new feb.Response(deform);

                // calculate the displacement
                response.updateNodalDisplacements();

                // calculate the member forces
                response.updateMemberForces();

                // maxDisp = response.maxDisplacement();

                // this guards the objects from being freed prematurely
                GC.KeepAlive(deform);
                GC.KeepAlive(response);
            }
            catch
            {
                // send an error message in case something went wrong
                throw new Exception(singular_system_msg);
            }


            // --- output ---

            DA.SetData(0, new Karamba.GHopper.Models.GH_Model(model));

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
            get { return new Guid("f92e0814-909a-404b-bf32-aa93f7d12ef4"); }
        }
    }
}