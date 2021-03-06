﻿using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using DDAC_TP033375.Models;
using DDAC_TP033375.ViewModels;
using Microsoft.Ajax.Utilities;
using Microsoft.AspNet.Identity;

namespace DDAC_TP033375.Controllers
{
	public class BookingsController : Controller
	{
		private ApplicationDbContext _context;

		public BookingsController()
		{
			_context = new ApplicationDbContext();
		}

		protected override void Dispose(bool disposing)
		{
			_context.Dispose();
		}

		// GET: Bookings
		public ActionResult Index()
		{
			List<Booking> bookings;

			if (User.IsInRole(RoleName.Admin))
			{
				bookings = _context.Bookings
					.Include(b => b.BookedBy)
					.Include(b => b.Customer)
					.Include(b => b.Ship.Schedule)
					.ToList();
			}
			else
			{
				var currentUser = _context.Users.Find(User.Identity.GetUserId());

				bookings = _context.Bookings
					.Include(b => b.BookedBy)
					.Include(b => b.Customer)
					.Include(b => b.Ship.Schedule)
					.Where(b => b.BookedBy.CompanyName.Equals(currentUser.CompanyName))
					.ToList();
			}

			return View(bookings);
		}

		public ActionResult Details(int id)
		{
			var booking = _context.Bookings
				.Include(b => b.BookedBy)
				.Include(b => b.Customer)
				.Include(b => b.Containers)
				.Include(b => b.Schedule)
				.Include(b => b.Ship)
				.SingleOrDefault(b => b.Id == id);

			if (booking == null)
				return HttpNotFound();

			ViewBag.PercentageCompleted = CalPercentageCompleted(booking);

			return PartialView("_Details", booking);
		}

		public ActionResult New(int? id)
		{
			if (id == null)
			{
				TempData["Message"] = "Please select an existing customer or create a new one before proceeding to create booking.";

				return RedirectToAction("Index", "Customers");
			}

			var viewModel = new BookingFormViewModel
			{
				Customer = _context.Customers.Single(c => c.Id == id)
			};

			return View(viewModel);
		}

		[HttpPost]
		public ActionResult Create(BookingFormViewModel viewModel)
		{
			var containers = new List<Container>();
			var shipInDb = _context.Ships.Single(s => s.Id == viewModel.ShipId);
			var totalNumberOfContainer = 0;

			foreach (var containerId in viewModel.ContainerIds)
			{
				containers.Add(_context.Containers.Find(containerId));
			}

			foreach (var container in containers)
			{
				totalNumberOfContainer += container.Amount;
			}

			try
			{
				shipInDb.Containers = containers;
				shipInDb.NumberOfAvailableContainerBay -= totalNumberOfContainer;
				_context.SaveChanges();
			}
			catch (Exception ex)
			{
				return Json(new { success = false, responseText = "Fail to update ship containers.<br/><strong>Error:</strong> " + ex.Message }, JsonRequestBehavior.AllowGet);
			}

			var booking = new Booking
			{
				BookedBy = _context.Users.Find(User.Identity.GetUserId()),
				Customer = _context.Customers.Find(viewModel.CustomerId),
				Containers = containers,
				Schedule = _context.Schedules.Find(viewModel.ScheduleId),
				Ship = shipInDb,
				BookedAt = DateTime.Now
			};

			try
			{
				_context.Bookings.Add(booking);
				_context.SaveChanges();

				return Json(new { success = true, responseText = "Booking has been created successfully." }, JsonRequestBehavior.AllowGet);
			}
			catch (Exception ex)
			{
				return Json(new { success = false, responseText = "Booking Failed.<br/><strong>Error:</strong> " + ex.Message }, JsonRequestBehavior.AllowGet);
			}
		}

		[HttpPost]
		public ActionResult Archive(int id)
		{
			throw new NotImplementedException();
		}

		[HttpPost]
		public ActionResult CreateContainer(Container tempContainer)
		{
			var container = new Container
			{
				ItemType = tempContainer.ItemType,
				WeightInTonne = tempContainer.WeightInTonne,
				Amount = tempContainer.Amount
			};

			_context.Containers.Add(container);

			try
			{
				_context.SaveChanges();
				var newContainer = _context.Containers.Find(container.Id);

				return Json(new {newContainer, success = true, responseText = "Container created successfully." }, JsonRequestBehavior.AllowGet);
			}
			catch (Exception ex)
			{
				return Json(new { success = false, responseText = "Fail to create container.<br/><strong>Error:</strong> " + ex.Message }, JsonRequestBehavior.AllowGet);
			}
		}

		public ActionResult FillOrigin(int numberOfContainer)
		{
			var origins = GetShipsWithEnoughContainerBays(numberOfContainer)
				.Select(s => s.Schedule)
				.ToList()
				.DistinctBy(s => s.Origin);

			return Json(origins, JsonRequestBehavior.AllowGet);
		}

		public ActionResult FillDestination(string origin, int numberOfContainer)
		{
			var destinations = GetShipsWithEnoughContainerBays(numberOfContainer)
				.Select(s => s.Schedule)
				.Where(s => s.Origin == origin)
				.ToList()
				.DistinctBy(s => s.Destination);

			return Json(destinations, JsonRequestBehavior.AllowGet);
		}

		public ActionResult FillTime(string origin, string destination, int numberOfContainer)
		{
			var schedules = GetShipsWithEnoughContainerBays(numberOfContainer)
				.Select(s => s.Schedule)
				.Where(s => s.Origin == origin && s.Destination == destination)
				.ToList();

			return Json(schedules, JsonRequestBehavior.AllowGet);
		}

		public ActionResult FillShip(int scheduleId, int numberOfContainer)
		{
			var ships = GetShipsWithEnoughContainerBays(numberOfContainer)
				.Where(s => s.ScheduleId == scheduleId)
				.ToList();

			return Json(ships, JsonRequestBehavior.AllowGet);
		}

		[HttpPost]
		public ActionResult DeleteContainer(int id)
		{
			var containerInDb = _context.Containers.Single(c => c.Id == id);

			if (containerInDb == null)
				return Json(new { success = false, responseText = "Delete Failed.<br/><strong>Error:</strong> Record not found." }, JsonRequestBehavior.AllowGet);

			_context.Containers.Remove(containerInDb);

			try
			{
				_context.SaveChanges();

				return Json(new { success = true, responseText = "Container has been deleted successfully." }, JsonRequestBehavior.AllowGet);
			}
			catch (Exception ex)
			{
				return Json(new { success = false, responseText = "Delete Failed.<br/><strong>Error:</strong> " + ex.Message }, JsonRequestBehavior.AllowGet);
			}
		}

		private IEnumerable<Ship> GetShipsWithEnoughContainerBays(int numberOfContainer)
		{
			return _context.Ships
				.Where(s => s.IsScheduled)
				.Where(s => s.NumberOfAvailableContainerBay >= numberOfContainer)
				.Include(s => s.Schedule)
				.ToList();
		}

		private static int CalPercentageCompleted(Booking booking)
		{
			var ending = (booking.Schedule.ArrivalTime - booking.Schedule.DepartureTime).TotalSeconds;
			var completed = (DateTime.Now - booking.Schedule.DepartureTime).TotalSeconds;
			var percentageCompleted = (completed / ending) * 100;

			if (percentageCompleted < 0)
			{
				return 0;
			}

			if (percentageCompleted > 100)
			{
				return 100;
			}

			return (int) percentageCompleted;
		}
	}
}