using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;

using Karamba.Loads;
using Karamba.Models;
using Karamba.Elements;
using Karamba.Supports;
using Karamba.Geometry;
using Karamba.CrossSections;
using KarambaCommon.Factories;
using Proj_JCP.Properties;

namespace Proj_JCP
{
    public class AddAditionalLoadCases : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the AddAditionalLoadCases class.
        /// </summary>
        public AddAditionalLoadCases()
          : base("AddAditionalLoadCases", "Nickname",
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
            pManager.AddParameter(new Karamba.GHopper.Loads.Param_Load(), "loads", "loads", "", GH_ParamAccess.list);
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
            List<Karamba.GHopper.Loads.GH_Load> gh_loads = new List<Karamba.GHopper.Loads.GH_Load>();

            List<Support> supports = new List<Support>();


            // --- input ---
            if (!DA.GetData(0, ref in_gh_model)) return;
            Model model = in_gh_model.Value;
            if (!DA.GetDataList(1, gh_loads)) return;

            // --- solve ---
            // clone model to avoid side effects
            model = model.Clone();

            // clone its elements to avoid side effects
            model.cloneElements();

            // clone the feb-model to avoid side effects
            model.deepCloneFEModel();

            string singular_system_msg = "The stiffness matrix of the system is singular.";

            var loads = gh_loads.Select(x => x.Value);

            
            foreach (Support s in model.supports)
            {
                Point3 pos = s.position;
                IReadOnlyList<bool> cond = s.Condition;
                Plane3 pln = s.plane();

                Support sup = new Support(pos, cond, pln);
                supports.Add(sup);
            }

            List<Line3> lines = new List<Line3>();

            var k3d = new KarambaCommon.Toolkit();

            List<Line3> L3sB = new List<Line3>();
            List<string> BeamIds = new List<string>();
            List<CroSec> CroSecs_B = new List<CroSec>();

            List<Line3> L3sT = new List<Line3>();
            List<string> TrussIds = new List<string>();
            List<CroSec> CroSecs_T = new List<CroSec>();

            List<Mesh3> M3s = new List<Mesh3>();
            List<string> ShellIds = new List<string>();
            List<CroSec> CroSecs_S = new List<CroSec>();

            List<BuilderElement> be = new List<BuilderElement>();

            var nodes = new List<Point3>();
            var logger = new Karamba.Utilities.MessageLogger();

            List<bool> isactivelst = new List<bool>();
            List<bool> isBeam = new List<bool>();
            foreach (ModelElement e in model.elems)
            {

                if (e as ModelShell != null)
                {
                    ModelShell shell = e as ModelShell;

                    M3s.Add(shell.mesh as Mesh3);
                    ShellIds.Add(shell.id);
                    CroSecs_S.Add(shell.crosec);
                    
                }
                else
                {
                    var line = new Line3(model.nodes[e.node_inds[0]].pos,
                      model.nodes[e.node_inds[1]].pos);

                    ModelTruss mt = e as ModelTruss;
                    bool isBendingStiff = mt.bending_stiff;

                    if (isBendingStiff)
                    {
                        L3sB.Add(line);
                        BeamIds.Add(e.id);
                        CroSecs_B.Add(e.crosec);
                    }

                    else
                    {
                        L3sT.Add(line);
                        TrussIds.Add(e.id);
                        CroSecs_T.Add(e.crosec);
                    }

                    isactivelst.Add(e.is_active);
                    
                }
            }

            var elems_b = k3d.Part.LineToBeam(L3sB, BeamIds, CroSecs_B, logger, out nodes, true, 0.005, null, null, null, true, false).Select(x => x as BuilderElement);

            var elems_t = k3d.Part.LineToBeam(L3sT, TrussIds, CroSecs_T, logger, out nodes, false, 0.005, null, null, null, true, false).Select(x => x as BuilderElement);

            var elems_s = k3d.Part.MeshToShell(M3s, ShellIds, CroSecs_S, logger, out nodes)
              .Select(x => x as BuilderElement);

            be.AddRange(elems_b);
            be.AddRange(elems_t);
            be.AddRange(elems_s);


            var lds = loads.ToList();

            var new_model = k3d.Model.AssembleModel
                (be, supports, lds, out string info, out double mass, 
                out Point3 cog, out info, out bool flag);
            int cnt = 0;

            for (int i = 0; i < new_model.elems.Count; i++)
            {

                if (new_model.elems[i] as ModelShell != null)
                { 
                
                }
                else
                {
                    new_model.elems[i].set_is_active(new_model, isactivelst[cnt]);
                    cnt++;
                }

            }

            // update the model to its final state
            try
            {
                new_model.febmodel.touch();
                // create a deform and response object for calculating and retrieving results
                feb.Deform deform = new feb.Deform(new_model.febmodel);
                // feb.Deform deform = new feb.Deform(model.febmodel);
                feb.Response response = new feb.Response(deform);

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
            DA.SetData(0, new Karamba.GHopper.Models.GH_Model(new_model));
            // DA.SetData(0, new Karamba.GHopper.Models.GH_Model(model));
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
                return Resources.addloads;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("544c2a19-3fa6-433b-b659-74728a6d2336"); }
        }
    }
}